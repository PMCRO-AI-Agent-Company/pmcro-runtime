// Tools/PmcroCycleSkill.cs
// Canonical PMCR-O cycle entry point. Execution is owned by Microsoft Agent
// Framework's declarative workflow runtime; this skill is only the governed
// invocation surface exposed to the Orchestrator agent.
//
// The previous implementation called PmcroLoop.RunAsync directly. That created
// a competing hand-rolled execution engine beside MAF's declarative workflow
// engine and was the source of an architectural split. The declarative workflow
// now owns phase routing, MCP execution, evidence capture, HIL, and cycle
// execution. PmcroLoop remains available as a migration/reference implementation
// until the live regression suite proves the declarative path equivalent.

using System.ComponentModel;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;
using ProjectName.OrchestratorService.Services;
using ProjectName.OrchestratorService.Workflows.Declarative;

namespace ProjectName.OrchestratorService.Tools;

public sealed class PmcroCycleSkill(
    DeclarativeCycleRunner declarativeRunner,
    ISubjectAgentRegistry registry,
    ILogger<PmcroCycleSkill> logger) : AgentClassSkill<PmcroCycleSkill>
{
    private static readonly HashSet<string> ChiefDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "ceo", "chief-of-staff", "cto", "coo", "cfo", "cro", "cmo", "clo", "chro", "domain-specialist",
    };

    public override AgentSkillFrontmatter Frontmatter { get; } = new(
        name: "pmcro-cycle",
        description: "Runs one governed PMCR-O cycle through the canonical Microsoft Agent Framework declarative workflow. " +
                     "The workflow owns Plan -> Make -> Check -> Reflect execution, MCP evidence, HIL, trail sealing, " +
                     "and disposition. This is the only governed action entry point exposed to the Orchestrator agent.");

    protected override string Instructions => """
        You are the PMCR-O Orchestrator. To take governed real action, call
        run_pmcro_cycle with a clear, complete seed_intent describing exactly what
        should happen. Do not implement the plan yourself and do not call MCP
        actuators directly.

        The cycle is executed by the Microsoft Agent Framework declarative workflow.
        The workflow is authoritative for phase routing, MCP invocation, evidence
        capture, Checker coverage, HIL, Reflector disposition, and trail sealing.
        Do not bypass it with direct filesystem, terminal, browser, or custom loop
        calls.

        PATH VERBATIM RULE: if the user's message contains a filesystem path, copy
        it into seed_intent character-for-character. Treat paths as opaque tokens.

        After calling run_pmcro_cycle, report the returned disposition and final
        output honestly. RETRY and HALT are not success.
        """;

    [AgentSkillScript("run_pmcro_cycle")]
    [Description(
        "Runs one governed Plan->Make->Check->Reflect cycle using the canonical " +
        "Microsoft Agent Framework declarative workflow. Returns trail_id, " +
        "disposition (ACCEPT/RETRY/HALT), final_output, and retry/halt context.")]
    public async Task<string> RunPmcroCycleAsync(
        [Description("Clear, complete description of what should happen.")] string seedIntent,
        [Description("Project name this cycle belongs to.")] string project,
        [Description("Subject agent that should execute the atomic action, e.g. filesystem-agent.")] string subjectAgent = "filesystem-agent",
        [Description("Optional caller-supplied trail id for correlation. If omitted, the declarative runner creates one.")] string? trailId = null)
    {
        trailId ??= Guid.NewGuid().ToString();

        logger.LogInformation(
            "[Cycle] run_pmcro_cycle -> MAF declarative workflow — trail={Trail} intent=\"{Intent}\"",
            trailId, seedIntent);

        var resolved = registry.Resolve(subjectAgent);
        if (resolved is null && !ChiefDomains.Contains(subjectAgent))
            throw new InvalidOperationException(
                $"No AIAgent registered for subjectAgent='{subjectAgent}'. Register it before dispatching a cycle.");

        var result = await declarativeRunner.RunAsync(seedIntent, subjectAgent, project);

        logger.LogInformation(
            "[Cycle] MAF declarative workflow completed — requestedTrail={RequestedTrail} disposition={Disp}",
            trailId, result.Disposition);

        return System.Text.Json.JsonSerializer.Serialize(new
        {
            trail_id = trailId,
            disposition = result.Disposition.ToString().ToUpperInvariant(),
            final_output = result.FinalOutput,
            retry_context = result.RetryContext,
            halt_reason = result.HaltReason,
            cycle_number = result.CycleNumber,
            execution_engine = "Microsoft.Agents.AI.Workflows.Declarative"
        });
    }
}
