// Tools/PmcroCycleSkill.cs
// Replaces FilesystemAgentSkill as the tool surface exposed to the Orchestrator
// agent. Instead of exposing individual fs_* actions (fs_write_file, fs_promote,
// fs_delete, fs_zip, fs_read_file, fs_list_directory, source_dump) as separately
// callable tools, this skill exposes exactly one tool: run_pmcro_cycle.
//
// Calling run_pmcro_cycle triggers a full Plan -> Make -> Check -> Reflect cycle
// via PmcroLoop.RunAsync, which internally drives its own LLM calls per phase
// and (via McpToolCache) makes real MCP tool calls against mcp-filesystem during
// the Make phase. The Orchestrator agent's single turn therefore nests an entire
// cognitive loop inside one tool call, rather than dispatching flat fs_* actions
// directly.
//
// Trail logging: PmcroLoop.RunAsync writes a sealed trail frame (GUID folder,
// phase JSONL, disposition.json) via ITrailWriter for every invocation — see
// Loop/TrailWriter.cs. This is what makes a cycle's outcome auditable rather
// than self-reported.
//
// Decision recorded 2026-06-20: fs_* tools are intentionally no longer reachable
// directly from Orchestrator. Only PmcroLoop's internal MakeAsync (via
// McpToolCache) can write/read files now.

using System.ComponentModel;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;
using ProjectName.OrchestratorService.Loop;
using ProjectName.OrchestratorService.Services;

namespace ProjectName.OrchestratorService.Tools;

public sealed class PmcroCycleSkill(
    PmcroLoop                       loop,
    ISubjectAgentRegistry           registry,
    ILogger<PmcroCycleSkill>        logger) : AgentClassSkill<PmcroCycleSkill>
{
    // ARCH-CHIEF-CODEACT-001 (2026-07-22): the 10 C-Suite/Staff/Domain tags
    // DomainSelector.tsx can send (mirrors DOMAINS in that file exactly).
    // None of these resolve to a live AIAgent in SubjectAgentRegistry today,
    // so an unresolvable chief tag falls back to codeact-agent instead of
    // filesystem-agent below -- sandboxed compute (read-only tools +
    // execute_code) rather than raw file ops, while HIL-gated writes still
    // go through the existing WriteFile TYPE1_PENDING path. subjectAgentName
    // (passed through to PmcroLoop.RunAsync unchanged) still carries the
    // original tag, so trail attribution (.pmcro/trails/<domain>/) is
    // unaffected by this fallback change.
    private static readonly HashSet<string> ChiefDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "ceo", "chief-of-staff", "cto", "coo", "cfo", "cro", "cmo", "clo", "chro", "domain-specialist",
    };

    public override AgentSkillFrontmatter Frontmatter { get; } = new(
        name:        "pmcro-cycle",
        description: "Runs a full PMCR-O cognitive cycle (Plan -> Make -> Check -> Reflect) for a given " +
                     "intent. Use this whenever the user's request requires taking real action " +
                     "(creating, modifying, or inspecting files) rather than just answering a question. " +
                     "The cycle plans the steps, executes them via real filesystem tools, independently " +
                     "verifies the result, and issues a final disposition (ACCEPT/RETRY/HALT). " +
                     "This is the only way to take real action — there is no direct file-write tool.");

    protected override string Instructions => """
        You are the PMCR-O Orchestrator. To take any real action (file creation,
        modification, reading, or inspection), call run_pmcro_cycle with a clear,
        complete seed_intent describing exactly what should happen. Do not attempt
        to describe file contents yourself — the cycle's own Maker phase will
        determine and execute the concrete steps.

        PATH VERBATIM RULE (BUG-FIX-PATH-002, 2026-07-22): if the user's message
        contains a filesystem path, copy it into seed_intent character-for-character
        — every backslash, every dot, every segment — exactly as the user wrote it.
        Do NOT retype, reformat, "clean up", or paraphrase a path from memory; treat
        it as an opaque token, not prose. A single dropped or added character (e.g.
        losing the backslash before ".pmcro", or duplicating a folder name) produces
        a path to a file that does not exist, which fails the whole cycle for a
        reason that has nothing to do with the actual request. If you are not fully
        confident you copied a path exactly, quote it back in seed_intent inside
        backticks so downstream stages can see it was taken verbatim, e.g.
        `W:\PMCR-O\.pmcro\trails\...\disposition.json`.

        After calling run_pmcro_cycle, report the cycle's disposition and final
        output to the user honestly. If disposition is RETRY or HALT, say so
        plainly — do not present a non-ACCEPT cycle as having succeeded.
        """;

    [AgentSkillScript("run_pmcro_cycle")]
    [Description(
        "Runs one full Plan->Make->Check->Reflect cycle for the given intent. " +
        "Returns trail_id, disposition (ACCEPT/RETRY/HALT), final_output, and " +
        "halt_reason/retry_context if applicable. This is the only way to take " +
        "real filesystem action — there is no separate direct write tool.")]
    public async Task<string> RunPmcroCycleAsync(
        [Description("Clear, complete description of what should happen — e.g. 'Create a file named test.txt in staging containing the text Hello World.'")] string seedIntent,
        [Description("Project name this cycle belongs to, e.g. 'pmcro-agent-system'.")] string project,
        [Description("Subject agent that should execute the steps, e.g. 'filesystem-agent'.")] string subjectAgent = "filesystem-agent",
        [Description("Optional caller-supplied trail id for correlating this cycle's trail folder (S:\\.pmcro\\trails\\<trail_id>) with an external test or tracking id. If omitted, a new GUID is generated.")] string? trailId = null)
    {
        trailId ??= Guid.NewGuid().ToString();

        logger.LogInformation(
            "[Cycle] run_pmcro_cycle invoked — trail={Trail} intent=\"{Intent}\"",
            trailId, seedIntent);

        var subjectAgentInstance = registry.Resolve(subjectAgent)
            ?? (ChiefDomains.Contains(subjectAgent) ? registry.Resolve("codeact-agent") : null)
            ?? registry.Resolve("filesystem-agent")
            ?? throw new InvalidOperationException(
                $"No AIAgent registered for subjectAgent='{subjectAgent}'. Register it in Program.cs.");

        var result = await loop.RunAsync(seedIntent, trailId, project, subjectAgent, subjectAgentInstance);

        logger.LogInformation(
            "[Cycle] trail={Trail} disposition={Disp}",
            trailId, result.Disposition);

        return System.Text.Json.JsonSerializer.Serialize(new
        {
            trail_id      = trailId,
            disposition   = result.Disposition.ToString().ToUpperInvariant(),
            final_output  = result.FinalOutput,
            retry_context = result.RetryContext,
            halt_reason   = result.HaltReason,
            cycle_number  = result.CycleNumber
        });
    }
}
