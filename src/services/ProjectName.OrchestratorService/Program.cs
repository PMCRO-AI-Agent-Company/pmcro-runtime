using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Agents.AI.Hosting.OpenAI;
using Microsoft.Agents.AI.Hosting.AGUI.AspNetCore;
using Microsoft.Agents.AI.DevUI;
using Microsoft.Agents.AI.Hyperlight;
// ARCH-HARNESS-001 fix (2026-07-15, CS0234): 'Microsoft.Agents.AI.Harness' is
// NOT a real namespace in this package -- AsHarnessAgent/HarnessAgentOptions
// ship directly under the root Microsoft.Agents.AI namespace (already
// imported below), unlike Hyperlight which does get its own sub-namespace.
// If a future package version reintroduces a dedicated namespace, the build
// error will name it and this comment can be deleted.
using HyperlightSandbox.Guest.Python;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using ProjectName.OrchestratorService.Configuration;
using ProjectName.OrchestratorService.Loop;
using ProjectName.OrchestratorService.Services;
using ProjectName.OrchestratorService.Skills;
using ProjectName.OrchestratorService.Tools;
using ProjectName.ServiceDefaults;
using OllamaSharp;

// ARCH-DECLARATIVE-001 (2026-08-06): standalone load-test entry point for
// DeclarativeWorkflowBuilder.Build<T>() against pattern-a-macro-cycle.yaml.
// Deliberately short-circuits BEFORE any Ollama/MCP/DI wiring below -- Build()
// only parses/compiles the YAML into a Workflow graph, it doesn't invoke
// agents, so none of that infrastructure needs to be running for this check.
if (args.Contains("--validate-declarative"))
{
    Environment.Exit(ProjectName.OrchestratorService.Workflows.Declarative.ValidationHarness.ValidatePatternA());
}
if (args.Contains("--run-declarative"))
{
    Environment.Exit(await ProjectName.OrchestratorService.Workflows.Declarative.ValidationHarness.RunPatternA());
}
// TEMP DEBUG (ARCH-DECLARATIVE-013 investigation, remove after use): isolated
// test of the exact Find/Char/Mid primitives extract_instruction uses, against
// hardcoded text with no LLM/agent variance at all. No agents needed to build
// or run (same reasoning as --validate-declarative above), and every result is
// reported via a LITERAL (non-formula) SendActivity string, immune to the
// unevaluated-formula console-dump quirk flagged on cycle_start/debug_dump_*.
if (args.Contains("--test-stringops"))
{
    var yamlPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
        "..", "..", "..", "Workflows", "Declarative", "test-stringops.yaml"));
    Console.WriteLine($"[test-stringops] YAML path: {yamlPath}");
    var registry = new ProjectName.OrchestratorService.Services.SubjectAgentRegistry();
    var provider = new ProjectName.OrchestratorService.Workflows.Declarative.SubjectAgentRegistryProvider(registry);
    var options = new Microsoft.Agents.AI.Workflows.Declarative.DeclarativeWorkflowOptions(provider);
    try
    {
        var workflow = Microsoft.Agents.AI.Workflows.Declarative.DeclarativeWorkflowBuilder.Build<string>(yamlPath, options);
        Console.WriteLine("[test-stringops] Build OK. Starting run...");
        Microsoft.Agents.AI.Workflows.StreamingRun run = await Microsoft.Agents.AI.Workflows.InProcessExecution.RunStreamingAsync(workflow, "unused");
        await run.TrySendMessageAsync(new Microsoft.Agents.AI.Workflows.TurnToken(emitEvents: true));
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await foreach (var evt in run.WatchStreamAsync().WithCancellation(cts.Token))
        {
            if (evt is Microsoft.Agents.AI.Workflows.ExecutorFailedEvent or Microsoft.Agents.AI.Workflows.WorkflowErrorEvent)
            {
                Console.WriteLine($"[test-stringops] FAILURE EVENT: {evt.GetType().Name}");
                var exObj = evt switch
                {
                    Microsoft.Agents.AI.Workflows.ExecutorFailedEvent f => (object?)f.Data,
                    Microsoft.Agents.AI.Workflows.WorkflowErrorEvent w => w.Data,
                    _ => null
                };
                if (exObj is Exception ex)
                {
                    Console.WriteLine($"[test-stringops] {ex.GetType().FullName}: {ex.Message}");
                    Console.WriteLine(ex.StackTrace);
                    if (ex.InnerException is not null)
                        Console.WriteLine($"[test-stringops] Inner: {ex.InnerException.GetType().FullName}: {ex.InnerException.Message}\n{ex.InnerException.StackTrace}");
                }
            }
            else
            {
                Console.WriteLine($"[test-stringops] EVENT: {evt.GetType().Name} -> {evt}");
            }
        }
        Console.WriteLine("[test-stringops] Stream completed.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[test-stringops] EXCEPTION: {ex.GetType().FullName}: {ex.Message}");
        Console.WriteLine(ex.StackTrace);
    }
    Environment.Exit(0);
}

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// EC-DEVUI-001: AddDevUI() MUST precede AddOpenAIResponses() and AddOpenAIConversations().
// AddDevUI() registers the InputMessageContentJsonConverter discriminator table that
// includes "function_approval_response". If it runs after, that discriminator is absent
// when the OpenAI Responses endpoint deserializes a HIL approval POST body → HTTP 400.
// NOTE: AddDevUI is an IHostApplicationBuilder extension (not IServiceCollection).
builder.AddDevUI();
builder.AddOpenAIResponses();
builder.AddOpenAIConversations();

// ARCH-AGUI-001 (2026-07-11): registers AG-UI protocol services (the actual
// protocol CopilotKit's runtime speaks — replaces the earlier hand-rolled
// OpenAI-shim in ProjectName.OrchestratorApi/Controllers/CopilotController.cs,
// which predates Microsoft's official AG-UI hosting package). Placed after
// AddDevUI() defensively, matching EC-DEVUI-001's ordering concern above —
// this hasn't surfaced a conflict in testing, but AddAGUIServer's own converter
// registration order relative to AddDevUI's is unverified against Microsoft's
// docs, so if a similar 400-on-deserialize bug appears on the /agui endpoint,
// try moving this line above AddDevUI() first.
//
// RENAME (2026-07-22, 1.14.0 upgrade): AddAGUI() -> AddAGUIServer() and
// MapAGUI() -> MapAGUIServer() in Microsoft.Agents.AI.Hosting.AGUI.AspNetCore
// 1.14.0-preview.260721.1 (was 1.13.0-preview.260703.1). Confirmed via
// AGUIServerServiceCollectionExtensions / AGUIEndpointRouteBuilderExtensions
// in the restored package DLL -- no other behavioral change found in the
// exposed type/method surface (AGUIStreamOptions, AsAGUIEventStreamAsync,
// ChatResponseUpdateAGUIExtensions, ConfigureAGUIJsonOptions all unchanged).
builder.Services.AddAGUIServer();

// ── Configuration ─────────────────────────────────────────────────────────────
builder.Services.Configure<OrchestratorConfig>(
    builder.Configuration.GetSection(OrchestratorConfig.SectionName));

// ── Infrastructure ────────────────────────────────────────────────────────────
builder.Services.AddSingleton<ITrailWriter, FileTrailWriter>();

// ARCH-NATIVE-MAF-001 (2026-07-20): SkillManifestReader replaces PmcroSkillLoader.
// The full skill lifecycle (advertise, load_skill, read_skill_resource, run_skill_script)
// is now handled natively by MAF's AgentSkillsProvider via MarketplaceSkillsMaterializer.
// SkillManifestReader only extracts Colony Laws for subject agent instruction composition.
builder.Services.AddSingleton<SkillManifestReader>();

// ARCH-MARKETPLACE-BRIDGE-001 (2026-07-20): bridges .agents/plugins/marketplace.json
// to MAF's native AgentSkillsProvider. See Skills/MarketplaceSkillsMaterializer.cs for
// why this replaced the old build-time csproj Content glob (it pointed at a
// nonexistent repo-root skills/ folder and silently materialized nothing).
builder.Services.AddSingleton<MarketplaceSkillsMaterializer>();
builder.Services.AddSingleton<SkillCatalogService>();
builder.Services.AddHealthChecks()
    .AddCheck<SkillStagingHealthCheck>("maf-skill-staging", tags: ["ready"]);
builder.Services.AddHostedService<MarketplaceSkillsWatcherService>();

builder.Services.AddGrpc();

// ARCH-CTRL-001: HIL approve/deny moved out of Program.cs into Controllers/HilController.cs
// (see that file for the Development-only gate that replaces the old inline
// `if (app.Environment.IsDevelopment())` wrapper around the MapPost calls).
builder.Services.AddControllers();

// ── HIL ───────────────────────────────────────────────────────────────────────
builder.Services.AddSingleton<DevUiHilChannel>();
builder.Services.AddSingleton<IHilChannel>(sp => sp.GetRequiredService<DevUiHilChannel>());

// ── MCP HTTP clients ──────────────────────────────────────────────────────
builder.Services.AddHttpClient("mcp-filesystem", client =>
    client.BaseAddress = new Uri("http://mcp-filesystem"))
    .AddServiceDiscovery();
builder.Services.AddHttpClient("mcp-terminal", client =>
    client.BaseAddress = new Uri("http://mcp-terminal"))
    .AddServiceDiscovery();
// EC-ASPIRE-001 / EC-MCP-PLAYWRIGHT-001: mcp-playwright needs a generous timeout.
// The default AddStandardResilienceHandler() applies a 30s AttemptTimeout globally.
// ExecuteNavigateTo cold-starts Chromium (~3-5s) THEN navigates — total can exceed 30s.
// Strip the global pipeline and replace with a single 90s timeout. No retry (idempotency
// concern: a second NavigateTo would double-navigate). Circuit breaker also disabled:
// playwright cycles are rare enough that trip/recovery doesn't help.
var pwClientBuilder = builder.Services.AddHttpClient("mcp-playwright", client =>
{
    client.BaseAddress = new Uri("http://mcp-playwright");
    client.Timeout = TimeSpan.FromSeconds(90);
});
pwClientBuilder.RemoveAllResilienceHandlers();
pwClientBuilder.AddServiceDiscovery();

builder.Services.AddSingleton<McpToolCache>();

// ── LLM ───────────────────────────────────────────────────────────────────────
builder.Services.AddKeyedSingleton<IChatClient>("ollama", (sp, _) =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var cs = cfg.GetConnectionString("ollama-server") ?? "http://localhost:11434";
    var endpoint = cs.StartsWith("Endpoint=", StringComparison.OrdinalIgnoreCase)
        ? cs["Endpoint=".Length..]
        : cs;
    if (!endpoint.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        endpoint = "http://" + endpoint;
    IChatClient rawClient = new OllamaApiClient(new Uri(endpoint)) { SelectedModel = "qwen3:8b" };

    // FIX-3 (Maker Death Spiral): cap the SDK's internal function-invocation loop
    // to a single round-trip per request. Without this, FunctionInvokingChatClient
    // (the client Microsoft.Agents.AI wraps IChatClient in under the hood) will
    // keep re-issuing tool calls on failure until MaximumIterationsPerRequest is
    // hit (default 40) or the model stops asking — that 40-step retry spiral is
    // exactly what produced the \project1\project1 nested-path hallucinations.
    // Explicit IChatClient cast is required: OllamaApiClient itself exposes an
    // ambiguous .AsBuilder() overload (see CS0121 note on the earlier .AsBuilder()
    // fix elsewhere in this codebase) — casting to IChatClient first resolves to
    // the correct ChatClientBuilderChatClientExtensions.AsBuilder(IChatClient) overload.
    return rawClient.AsBuilder()
        .UseFunctionInvocation(configure: c => c.MaximumIterationsPerRequest = 1)
        .Build();
});
builder.Services.AddSingleton<IChatClient>(
    sp => sp.GetRequiredKeyedService<IChatClient>("ollama"));

// ── LLM (harness-agent variant) ────────────────────────────────────────────
// ARCH-HARNESS-001 (2026-07-15): the shared "ollama" IChatClient above is
// deliberately capped at MaximumIterationsPerRequest=1 (FIX-3, Maker Death
// Spiral) for the split-turn PMCRO subject agents, which each get exactly one
// tool call per invocation. AsHarnessAgent's whole value proposition is an
// autonomous multi-turn tool loop (function invocation, todo-driven
// plan/execute, progressive skill loading) -- reusing the capped client would
// silently neuter it to a single tool call, same as the subject agents. This
// second keyed client is the same Ollama endpoint/model with a bounded-but-real
// iteration budget instead, used ONLY by harness-agent below.
builder.Services.AddKeyedSingleton<IChatClient>("ollama-harness", (sp, _) =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var cs = cfg.GetConnectionString("ollama-server") ?? "http://localhost:11434";
    var endpoint = cs.StartsWith("Endpoint=", StringComparison.OrdinalIgnoreCase)
        ? cs["Endpoint=".Length..]
        : cs;
    if (!endpoint.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        endpoint = "http://" + endpoint;
    IChatClient rawClient = new OllamaApiClient(new Uri(endpoint)) { SelectedModel = "qwen3:8b" };

    return rawClient.AsBuilder()
        .UseFunctionInvocation(configure: c => c.MaximumIterationsPerRequest = 25)
        .Build();
});

// ── MAF-native PMCRO loop ─────────────────────────────────────────────────────
builder.Services.AddSingleton<PmcroLoop>();

// ARCH-DECLARATIVE-001 (2026-08-06): bridges DeclarativeWorkflowBuilder-loaded
// YAML workflows to the same local ISubjectAgentRegistry every other MAF-native
// path uses, instead of Foundry cloud (AzureAgentProvider). Registered here so
// it's available once a declarative-workflow entry point is wired up (still
// pending — see Workflows/Declarative/pattern-a-macro-cycle.yaml).
builder.Services.AddSingleton<ProjectName.OrchestratorService.Workflows.Declarative.SubjectAgentRegistryProvider>();

// ARCH-DECLARATIVE-003 (2026-08-06): real integration -- runs the declarative
// YAML path through the same ITrailWriter/FileTrailWriter sealed-trail
// mechanism as PmcroCycleWorkflow, instead of only proving parse/run via
// console output (ValidationHarness). Depends on the real ISubjectAgentRegistry
// registered below, so it can only be constructed after that registration.
builder.Services.AddSingleton<ProjectName.OrchestratorService.Workflows.Declarative.DeclarativeCycleRunner>();

// ── Subject Agent Registry (Anthropic Orchestrator-Workers pattern) ─────────
// Each Colony subject agent is a true MAF AIAgent with its own name, tool set,
// and identity. The registry maps subjectAgent name → AIAgent instance so
// PmcroLoop wires the correct agent directly into the WorkflowBuilder graph.
// DevUI sees each subject agent by its registered Colony name.
builder.Services.AddSingleton<ISubjectAgentRegistry>(sp =>
{
    var cache = sp.GetRequiredService<McpToolCache>();
    var chat = sp.GetRequiredService<IChatClient>();
    var manifestReader = sp.GetRequiredService<SkillManifestReader>();
    var registry = new SubjectAgentRegistry();

    // GROUP 4: Prepend each subject agent's "## Colony Laws" section ahead of the hardcoded
    // JSON-schema/tool-call instructions below. We deliberately do NOT swap in the
    // *entire* FullManifest — filesystem-agent/terminal-agent SKILL.md files also
    // contain package-layout diagrams and "Future EarnedConstraints" boilerplate
    // that adds nothing at runtime and would bloat every cycle's prompt for a local
    // 8b model. Only the Colony Laws table is genuinely load-bearing philosophy;
    // the schema/tool-call text below stays the runtime contract (qwen3:8b-tuned
    // wording — "one tool", "no preamble", exact JSON keys BuildMakerFrame parses).
    static string ComposeSubjectInstructions(SkillManifestReader reader, string agentName, string schema)
    {
        var law = reader.ReadColonyLaws(agentName);
        if (law is null) return schema;
        return $"{law}\n\n---\n\n{schema}";
    }

    // ── filesystem-agent ──────────────────────────────────────────────────────
    // MAF-native AIAgent wrapping mcp-filesystem (Streamable HTTP, stateless).
    // Tool set: WriteFile (TYPE1) + ReadFile/ListDirectory/SearchFiles/etc (TYPE2).
    // Registered as "filesystem-agent" — matches SKILL.md dp_id and run_pmcro_cycle
    // routing string. DevUI shows "filesystem-agent" in the workflow graph.
    var fsTools = cache.GetMakerTools("filesystem-agent");
    var fsAgent = chat.AsAIAgent(new ChatClientAgentOptions
    {
        Name = "filesystem-agent",
        ChatOptions = new ChatOptions
        {
            Instructions = ComposeSubjectInstructions(manifestReader, "filesystem-agent", """
                You are the filesystem-agent — the Colony's subject agent for all
                filesystem operations. You execute the Planner's steps using the
                tools provided to you. Call tools directly and record real results.

                ARCH-DECLARATIVE-014 (2026-08-07): your input arrives as the
                Planner's raw JSON, e.g.
                {"step":{"action":"ListDirectory","inputs":["W:\\some\\path"]},
                 "instruction_for_subject":"Call ListDirectory on W:\\some\\path."}
                This is a JSON DESCRIPTION of what to do, not a request to describe
                it back. Read step.action as the exact tool name to call and
                step.inputs (or instruction_for_subject, if present) as its
                argument(s), then CALL THAT TOOL IMMEDIATELY as your first action.
                Do not output prose, do not restate the plan, do not return an
                execution_report with empty tool_calls unless the tool call itself
                genuinely failed.

                CRITICAL (WriteFile): copy content character-for-character — do NOT
                alter any character.

                LOG-BEFORE-ACT: Before WriteFile, log intent in execution_report.
                You get exactly ONE tool call per invocation (MaximumIterationsPerRequest=1) —
                do NOT attempt to call ReadFile after WriteFile to verify; you cannot, and must
                not claim to have done so.

                CRITICAL (HIL gate): WriteFile is a TYPE1 (mutating) action. When you call
                WriteFile, it returns a TYPE1_PENDING stub (the Orchestrator handles approval
                and the real write — exactly like playwright-agent's NavigateTo). Do NOT call
                the real write yourself. After calling WriteFile, output ONLY the JSON
                execution_report the tool returns (which contains the TYPE1_PENDING marker);
                the Orchestrator will execute the real write post-approval and the Checker
                audits the resulting file. Report the real bytes_written honestly in that
                execution_report. Verification against real file content is the Checker's job
                in a separate audit turn, or a later cycle's own planned ReadFile step — not
                yours to perform or narrate here.

                CRITICAL: Do NOT attempt to automatically retry failed tool calls. If a tool
                returns an error, STOP. Output your JSON execution_report immediately with the
                failure. NEVER hallucinate nested working directories (e.g. \project1\project1).

                Output schema:
                {
                  "artifact_type": "document | data | config | other",
                  "artifact": "...",
                  "execution_report": {
                    "steps_executed": 0,
                    "steps_skipped": 0,
                    "tool_calls": [{ "step_id": 1, "tool": "...", "result": "...", "bytes_written": 0 }],
                    "errors": []
                  }
                }
                Output valid JSON only.
                """),
            Tools = fsTools,
            AdditionalProperties = new() { ["think"] = (object)false },
        },
    });
    registry.Register("filesystem-agent", fsAgent);

    // ── terminal-agent ────────────────────────────────────────────────────────
    // MAF-native AIAgent wrapping mcp-terminal (Streamable HTTP, stateless).
    // Tool set: RunCommand + RunScript + KillProcess (TYPE1) +
    //           GetTerminalStatus + GetEnvironment + Which (TYPE2).
    // WorkingRoot is sandboxed to repoRoot via Parameters:working-root.
    var termTools = cache.GetMakerTools("terminal-agent");
    var termAgent = chat.AsAIAgent(new ChatClientAgentOptions
    {
        Name = "terminal-agent",
        ChatOptions = new ChatOptions
        {
            Instructions = ComposeSubjectInstructions(manifestReader, "terminal-agent", """
            You are the terminal-agent. You have ONE tool: RunCommand.
                Call RunCommand immediately with the command from the plan. No preamble.

            RunCommand(command, args, workingDirectory, slot)
              command = executable name (e.g. "dotnet")
              args    = arguments string (e.g. "--version")

                On Windows, use "cmd /c <command>" for shell builtins like dir, echo, or type.

                After the tool call, output ONLY this JSON:
            {
              "artifact_type": "command_output",
              "artifact": "<exact stdout here>",
              "execution_report": {
                "steps_executed": 1,
                "steps_skipped": 0,
                    "tool_calls": [{ "step_id": 1, "tool": "RunCommand", "result": "<stdout>", "exit_code": 0 }],
                "errors": []
              }
            }
            """),
            Tools = termTools,
            AdditionalProperties = new() { ["think"] = (object)false },
        },
    });
    registry.Register("terminal-agent", termAgent);

    // ── playwright-agent ──────────────────────────────────────────────────────────────────────────────
    // MAF-native AIAgent wrapping mcp-playwright (Streamable HTTP, stateless).
    // ARCH-NEW-002 (2026-07-03): all 5 tools are exposed (mirrors filesystem-agent's
    // proven multi-tool pattern) — PLAN-002 grounding constrains the Planner to pick
    // exactly ONE per cycle from the real VERIFIED_RESOURCES list.
    // BUG-PWINSTR-001 (2026-07-03): this agent's Instructions previously hardcoded
    // "You have ONE tool: NavigateTo... call NavigateTo(url) immediately" — a stale
    // leftover from the earlier EC-TOOLAGENT-002 single-tool proving stage that was
    // never updated when pwTools grew to 5 tools. Result: the Maker called NavigateTo
    // every single cycle regardless of what the Planner actually chose (observed trail
    // 5f08bc0a-7aed-4b7a-be44-c6ec29d1610d, 2026-07-03 — Planner correctly progressed
    // NavigateTo -> GetPageTitle -> GetPageContent across 3 cycles per the persisted
    // plan frames, but mcp-playwright's own server logs show NavigateTo/ExecuteNavigateTo
    // firing in all 3 cycles). Fix: route off the Planner's actual step.action instead
    // of hardcoding one tool name.
    var pwTools = cache.GetMakerTools("playwright-agent");
    var pwAgent = chat.AsAIAgent(new ChatClientAgentOptions
    {
        Name = "playwright-agent",
        ChatOptions = new ChatOptions
        {
            Instructions = ComposeSubjectInstructions(manifestReader, "playwright-agent", """
                You are the playwright-agent — the Colony's subject agent for browser
                automation. The Planner's message above specifies exactly ONE atomic
                action to take this cycle, in its "step" object's "action" field (and
                "inputs" for that action's parameters, e.g. the target url for NavigateTo).

                CRITICAL: call ONLY the tool named in the Planner's step.action for THIS
                cycle. Do NOT call NavigateTo unless step.action is exactly "NavigateTo".
                Do NOT re-navigate or take any action beyond the one the Planner selected,
                even if it seems helpful — the Planner is tracking multi-cycle progress;
                calling a different tool than the one selected discards that progress.

                BUG-PWSILENT-001 (2026-07-03): if step.action is a TYPE2 tool
                (GetSessionStatus, GetPageTitle, GetPageContent, GetPageSnapshot) and a
                PRIOR cycle's navigation failed or the browser session seems dead, you
                MUST still call that exact tool. Do NOT decide the outcome yourself and
                skip the call, and do NOT produce a text-only answer with no tool call —
                a prior cycle's failure does not tell you what THIS tool will return right
                now. Call the named tool every cycle, with no exceptions; let the tool's
                own real response (including its own error field if the session truly is
                unusable) be the evidence in your execution_report, not your own guess.

                BUG-PWREPEAT-001 (2026-07-03): each cycle plans a NEW step.action, which
                may differ from the tool you called last cycle (e.g. last cycle was
                GetSessionStatus, this cycle's step.action is GetPageTitle). Re-checking
                a tool you already have a recent result for is NOT a substitute for
                calling the tool this cycle's plan actually names — even if you believe
                you already know the answer from a prior cycle's result still visible in
                this conversation. Before calling any tool, re-read THIS cycle's
                step.action value and confirm the tool name you are about to call matches
                it exactly, character for character. If it does not match, you have the
                wrong tool — stop and call the correct one instead.

                Call the ONE tool matching this cycle's step.action — NavigateTo returns
                TYPE1_PENDING (expected; the Orchestrator handles approval and real
                execution), the rest (GetSessionStatus, GetPageTitle, GetPageContent,
                GetPageSnapshot) are TYPE2 reads.

                After the tool call, output ONLY this JSON:
                {
                  "artifact_type": "type1_pending | page_data | other",
                  "artifact": "<the tool's raw result>",
                  "execution_report": {
                    "steps_executed": 1,
                    "steps_skipped": 0,
                    "tool_calls": [{ "step_id": 1, "tool": "<the exact tool you called>", "result": "<result>" }],
                    "errors": []
                  }
                }
                Output valid JSON only.
                """),
            Tools = pwTools,
            AdditionalProperties = new() { ["think"] = (object)false },
        },
    });
    registry.Register("playwright-agent", pwAgent);

    // ── codeact-agent (ARCH-CODEACT-001, 2026-07-12) ──────────────────────────
    // First production wiring of Hyperlight/CodeAct. Verified on THIS host before
    // this code was written: raw HyperlightSandbox.Api sandbox creation + Python
    // guest execution succeeded end-to-end (see .agents/skills/harness-codeact/
    // SKILL.md for the full trail — nuget publish-bug history, restore proof, and
    // the standalone runtime verification). This wiring is deliberately additive:
    // filesystem-agent/terminal-agent/playwright-agent above are untouched, so
    // their existing per-tool-call HIL semantics don't change for anyone who
    // doesn't opt into subjectAgent="codeact-agent".
    //
    // Scoped to READ-ONLY tools only (GetReadTools()) for this first cut.
    // CodeAct approval is per execute_code call, not per call_tool(...) inside
    // it — exposing WriteFile here would let a single HIL approval authorize an
    // unbounded sequence of writes, which is a real downgrade from ARCH-FS-HIL-001's
    // per-write approval. Revisit only with a deliberate, reviewed decision.
    var codeActTools = cache.GetMakerTools("codeact-agent")
        .OfType<Microsoft.Extensions.AI.AIFunction>()
        .ToArray();

    var codeActOptions = HyperlightCodeActProviderOptions.CreateForWasm(PythonGuestModule.GetModulePath());
    codeActOptions.Tools = codeActTools;
    var codeActProvider = new HyperlightCodeActProvider(codeActOptions);

    // ARCH-CHIEF-SKILLS-001 (2026-07-22): Chiefs as skills, not agents.
    // Domain skills (CFO, CEO, COO, etc.) are materialized by
    // MarketplaceSkillsMaterializer from .agents/plugins/marketplace.json
    // (pmcro-csuite plugin) into StagingRoot, same as the Orchestrator/Harness
    // agents' skills providers below. This provider makes them available to
    // codeact-agent via load_skill / read_skill_resource. The Orchestrator
    // already passes the domain tag as subjectAgentName to PmcroLoop.RunAsync,
    // so the agent knows which domain skill to load for the current cycle
    // without an extra parameter.
    //
    // BUGFIX (2026-08-06): this previously pointed at
    // Path.Combine(FileSystemRoot, "skills") -- a raw top-level skills/ folder
    // that has never existed in this repo (skills live under
    // plugins/<plugin-name>/skills/<skill-name>/, per marketplace.json's
    // "source" fields). codeact-agent's domain skills were silently empty.
    // MarketplaceSkillsMaterializer.StagingRoot is the one real, working
    // staging directory already used by the Orchestrator/Harness agents.
    var domainSkillsProvider = new AgentSkillsProvider(
        sp.GetRequiredService<MarketplaceSkillsMaterializer>().StagingRoot,
        sp.GetRequiredService<AgentFileSkillScriptRunner>());

    var codeActAgent = chat.AsAIAgent(new ChatClientAgentOptions
    {
        Name = "codeact-agent",
        AIContextProviders = [codeActProvider, domainSkillsProvider],
        ChatOptions = new ChatOptions
        {
            // ARCH-CHIEF-ROUTING-001 (2026-08-06): codeact-agent previously had no
            // Instructions at all -- fine while it was only ever invoked directly,
            // but now that Program.cs's run_pmcro_cycle routes every chiefDomains
            // subjectAgent (cfo/cto/etc.) here, it needs to actually know what to
            // do with the "SUBJECT_AGENT: <chief>" line PmcroLoop.BuildCycleIntent
            // already puts in every cycle's prompt. domainSkillsProvider (an
            // AgentSkillsProvider, same mechanism HarnessAgent's harnessSkillsProvider
            // uses below) exposes load_skill/read_skill_resource as real tools
            // alongside execute_code -- this text is what tells the model to
            // actually call them for a chief cycle instead of ignoring SUBJECT_AGENT
            // and just improvising.
            Instructions =
                """
                You are the Colony's codeact-agent — Python execution via a sandboxed
                execute_code tool, plus load_skill/read_skill_resource for on-demand
                Colony skill content.

                CHIEF DOMAIN CYCLES: the prompt for every cycle includes a
                "SUBJECT_AGENT: <name>" line. If <name> is one of the nine C-Suite
                chief domains (ceo, cfo, cto, coo, cmo, cro, clo, chro,
                chief-of-staff), you are executing this cycle AS that chief:
                  1. Call load_skill(<name>) FIRST, before anything else this cycle.
                  2. Read that skill's Owns / Does-Not-Own scope and act inside it —
                     do not answer from general knowledge as if no skill exists, and
                     do not perform work that skill's own text says belongs to a
                     different chief.
                  3. Only reach for execute_code if the chief's task genuinely
                     requires running code; most chief-domain cycles are analysis,
                     drafting, or a decision, not code execution.
                If SUBJECT_AGENT is not one of these nine names, proceed as a normal
                codeact-agent cycle (load_skill only if a specific named skill is
                relevant) — do not invent a chief persona that wasn't asked for.

                Output valid JSON only, same execution_report shape the other
                subject agents use: artifact_type, artifact, execution_report
                {steps_executed, steps_skipped, tool_calls, errors}.
                """,
            AdditionalProperties = new() { ["think"] = (object)false },
        },
    });
    registry.Register("codeact-agent", codeActAgent);

    return registry;
});

// ── AgentSkillsProvider script runner (.ps1) ──────────────────────────────────
builder.Services.AddSingleton<AgentFileSkillScriptRunner>(_ =>
    async (skill, script, arguments, serviceProvider, cancellationToken) =>
    {
        var psi = new System.Diagnostics.ProcessStartInfo("pwsh.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("-File");
        psi.ArgumentList.Add(script.FullPath);
        using var proc = System.Diagnostics.Process.Start(psi)!;
        var stdout = await proc.StandardOutput.ReadToEndAsync(cancellationToken);
        await proc.WaitForExitAsync(cancellationToken);
        return stdout.Trim();
    });

// ── Orchestrator AIAgent ──────────────────────────────────────────────────────
// Top-level agent exposed via DevUI / OpenAI-compat endpoints.
// Holds one tool — run_pmcro_cycle — which resolves the correct subject AIAgent
// from ISubjectAgentRegistry and passes it to PmcroLoop.RunAsync.
// AgentSkillsProvider injects Colony SKILL.md context into the Orchestrator's
// context window (progressive disclosure / Augmented LLM pattern).
builder.Services.AddKeyedSingleton<AIAgent>("Orchestrator", (sp, _) =>
{
    var loop = sp.GetRequiredService<PmcroLoop>();
    var registry = sp.GetRequiredService<ISubjectAgentRegistry>();
    var scriptRunner = sp.GetRequiredService<AgentFileSkillScriptRunner>();
    var skillCatalog = sp.GetRequiredService<SkillCatalogService>();

    var getSkillCatalogTool = Microsoft.Extensions.AI.AIFunctionFactory.Create(
        (string? query = null) => skillCatalog.GetSnapshot(query),
        name: "get_skill_catalog",
        description: "Returns the authoritative unique SKILL.md catalog. Use for every question about skill count, names, plugins, or descriptions. Never generate a catalog from memory and never include command names as skills.");

    // ARCH-CHIEF-ROUTING-001 (2026-08-06): closes the gap ARCH-CHIEF-SKILLS-001
    // (2026-07-22, codeact-agent registration below) flagged but never wired up --
    // subjectAgent="cfo"/"cto"/etc. previously fell straight through to the
    // registry.Resolve("filesystem-agent") fallback below, so a "chief" trail's
    // content was actually filesystem-agent output wearing the chief's trail
    // folder name (FileTrailWriter still namespaces by the string you pass, per
    // ARCH-DOMAIN-SELECT-001's comment -- that part was already correct). The
    // nine names below are exactly the pmcro-csuite plugin's chief skill ids
    // (.claude-plugin/marketplace.json), the same ids domainSkillsProvider
    // materializes for codeact-agent's load_skill/read_skill_resource tools.
    var chiefDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "ceo", "cfo", "cto", "coo", "cmo", "cro", "clo", "chro", "chief-of-staff"
    };

    var runCycleTool = Microsoft.Extensions.AI.AIFunctionFactory.Create(
        async (string seedIntent, string project = "project1", string subjectAgent = "filesystem-agent") =>
        {
            var trailId = Guid.NewGuid().ToString();

            // EC-INTENT-001: Strip routing params the Orchestrator LLM may have
            // appended to seedIntent verbatim from the user message.
            var selectedSkillsMatch = System.Text.RegularExpressions.Regex.Match(
                seedIntent,
                @"^\[skills:\s*(?<skills>[^\]]+)\]\s*",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var selectedSkills = selectedSkillsMatch.Success
                ? selectedSkillsMatch.Groups["skills"].Value
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                : Array.Empty<string>();

            var cleanIntent = System.Text.RegularExpressions.Regex.Replace(
                seedIntent,
                @"^\[skills:\s*[^\]]+\]\s*",
                string.Empty,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            cleanIntent = System.Text.RegularExpressions.Regex.Replace(
                cleanIntent,
                @"\s*(project|subjectAgent)\s*=\s*\S+",
                string.Empty,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();

            if (selectedSkills.Length > 0)
            {
                cleanIntent = $"[Explicit MAF skills: {string.Join(", ", selectedSkills)}]\n{cleanIntent}";
            }

            // ARCH-MAF-NATIVE-001: Resolve the subject AIAgent from the registry.
            // ARCH-CHIEF-ROUTING-001 (2026-08-06): a chief domain (cfo/cto/etc.) has
            // no registry entry of its own by design (ARCH-CHIEF-SKILLS-001 — chiefs
            // are skills, not agents) — route it to codeact-agent, which is the one
            // subject agent wired with domainSkillsProvider's load_skill tool, instead
            // of letting it fall through to the filesystem-agent fallback below.
            // subjectAgent itself (the string) is untouched, so FileTrailWriter still
            // namespaces the trail under the chief's own name (e.g. .pmcro/trails/cfo/),
            // even though codeact-agent is the AIAgent instance actually executing it.
            var subjectAgentInstance =
                (chiefDomains.Contains(subjectAgent) ? registry.Resolve("codeact-agent") : registry.Resolve(subjectAgent))
                ?? registry.Resolve("filesystem-agent")
                ?? throw new InvalidOperationException(
                    $"No subject agent registered for '{subjectAgent}' and no filesystem-agent fallback.");

            var result = await loop.RunAsync(
                cleanIntent, trailId, project, subjectAgent, subjectAgentInstance);
            return System.Text.Json.JsonSerializer.Serialize(result);
        },
        name: "run_pmcro_cycle",
        description: "Universal task executor — call this for EVERY user request without exception. " +
                     "File operations, code generation, research, analysis, or any other task. " +
                     "Runs the full PMCRO cycle (Planner → filesystem-agent → Checker → Reflector). " +
                     "Returns trail_id, disposition, and final_output.");

    // ARCH-MARKETPLACE-BRIDGE-001: StagingRoot is the same physical directory
    // this constructor used to hardcode via Path.Combine(AppContext.BaseDirectory,
    // "skills") -- now populated at runtime from marketplace.json by
    // MarketplaceSkillsMaterializer (registered above) instead of a build-time
    // csproj glob that pointed at a nonexistent folder.
    var skillsProvider = new AgentSkillsProvider(
        sp.GetRequiredService<MarketplaceSkillsMaterializer>().StagingRoot,
        scriptRunner);

    return new ChatClientAgent(sp.GetRequiredService<IChatClient>(), new ChatClientAgentOptions
    {
        Name = "Orchestrator",
        AIContextProviders = [skillsProvider],
        ChatOptions = new ChatOptions
        {
            Tools = [runCycleTool, getSkillCatalogTool],
            AdditionalProperties = new() { ["think"] = (object)false },
            Instructions =
                """
                You are the PMCR-O Colony Orchestrator. You have one work tool, run_pmcro_cycle, and one read-only catalog tool, get_skill_catalog.

                META QUESTIONS: If the user asks about you directly — who or what you
                are, what you can do, how the Colony works — answer directly, in your
                own voice, as "the PMCR-O Colony Orchestrator". Do NOT call
                run_pmcro_cycle for these; there is no file/task work to delegate, and
                the tool's result describes a subject agent's work, not your own
                identity.

                ABSOLUTE RULE for actual work: for every other user request — file
                operations, research, code, analysis, or anything else that requires
                doing something — call run_pmcro_cycle immediately. Never refuse. Never
                explain why you cannot do something. Always delegate task work to the
                cycle.

                ARCH-MAF-SKILL-SELECT-001 (2026-08-06): the frontend skill picker may prefix
                the user's message with a literal "[skills: skill-a, skill-b]" tag.
                Treat every declared name as explicit context: preserve it in the
                run intent so the native AgentSkillsProvider can resolve it through
                the materialized .pmcro/skills-staging tree. Never claim a skill was
                loaded unless the provider actually loads it.

                ARCH-DOMAIN-SELECT-001 (2026-07-20): the frontend's domain
                selector may prefix the user's message with a literal
                "[domain: x]" tag, e.g. "[domain: cfo] draft next month's budget".
                If present: strip that exact prefix (including the trailing
                space) before using the remainder as seedIntent, and pass the
                tag's value x as subjectAgent verbatim — even though x (e.g.
                "cfo") is not yet a name SubjectAgentRegistry can resolve to a
                live agent. This is intentional for now: an unresolvable
                subjectAgent falls back to filesystem-agent for execution, but
                FileTrailWriter still names the sealed trail's directory after
                the exact string you pass, so the domain tag is preserved in
                the trail record even before domain-specific skill-loading
                exists. If no "[domain: x]" prefix is present, proceed exactly
                as the untagged rule below already did.

                When calling run_pmcro_cycle:
                - seedIntent   = the user's full request verbatim, with any
                                 "[domain: x]" prefix already stripped per above
                - project      = "project1" (always)
                - subjectAgent = the domain tag's value if present (see above);
                                 otherwise "filesystem-agent" for file/disk tasks
                                 (default), omitted otherwise

                The cycle routes to the correct subject agent automatically and returns
                the result. When relaying that result, speak in your own voice as the
                Orchestrator, summarizing what was done — do NOT claim the subject
                agent's identity (e.g. "I am the filesystem-agent") or present its
                internal fields (subjectAgent name, raw JSON keys, file paths) as though
                they describe you.
                """,
        },
    });
});

// ── Harness Agent (ARCH-HARNESS-001, 2026-07-15) ─────────────────────────────
// First production wiring of Microsoft.Agents.AI.Harness. Deliberately
// additive and parallel to the "Orchestrator" keyed agent above -- it does NOT
// go through PmcroLoop/WorkflowBuilder (the harness supplies its own complete
// agent loop: function invocation, history persistence, context compaction,
// todo-based plan/execute, progressive skill loading, tool approval). Running
// it *inside* PmcroLoop's own cycle would be a loop wrapping a loop, so it is
// exposed as an independent AG-UI surface instead (see MapAGUI("/agui/harness"
// below), not registered in ISubjectAgentRegistry.
//
// BUILD RISK FLAG (unverified against this repo's pinned
// Microsoft.Agents.AI.Harness 1.13.0-preview.260703.1, matching the existing
// "AgentSession vs AgentThread" flag on PmcroStateBridgeAgent.cs): the
// AsHarnessAgent(HarnessAgentOptions) extension method and the property names
// below (ChatOptions, AIContextProviders, DisableAgentSkillsProvider) match
// Microsoft's published docs as of 2026-07-08, but this package is a preview
// build -- if `dotnet build` reports a missing member, check the compiler
// error / IntelliSense for the renamed member and swap it in; the rest of this
// block is unaffected.
//
// Read-only by design for this first cut: exposes only mcpToolCache's shared
// read tools. No WriteFile/RunCommand/NavigateTo here -- the harness's own
// tool-approval mechanism is a SEPARATE system from this Colony's existing
// IHilChannel/DevUiHilChannel gate used by PmcroLoop's DispatchType1Async.
// Wiring a TYPE1 (mutating) tool through both systems at once is a real design
// decision, not a default -- revisit deliberately before adding one here.
builder.Services.AddKeyedSingleton<AIAgent>("HarnessAgent", (sp, _) =>
{
    var harnessChat = sp.GetRequiredKeyedService<IChatClient>("ollama-harness");
    var cache = sp.GetRequiredService<McpToolCache>();
    var scriptRunner = sp.GetRequiredService<AgentFileSkillScriptRunner>();
    var skillCatalog = sp.GetRequiredService<SkillCatalogService>();
    var getSkillCatalogTool = Microsoft.Extensions.AI.AIFunctionFactory.Create(
        (string? query = null) => skillCatalog.GetSnapshot(query),
        name: "get_skill_catalog",
        description: "Returns the authoritative marketplace skill catalog as structured data. Use for every catalog question; never include command names as skills.");

    // Real MAF Agent Skills -- points at the same on-disk skills tree the
    // Orchestrator's AgentSkillsProvider already uses. Unlike the subject
    // agents' SkillManifestReader (which extracts and eagerly injects a "Colony
    // Laws" excerpt because those agents get exactly one tool call), the
    // harness's multi-turn loop can genuinely afford progressive disclosure:
    // advertise -> load_skill -> read_skill_resource, spread across turns.
    // ARCH-MARKETPLACE-BRIDGE-001: same StagingRoot as the Orchestrator agent's
    // provider above -- one marketplace-driven skills tree shared by both.
    var harnessSkillsProvider = new AgentSkillsProvider(
        sp.GetRequiredService<MarketplaceSkillsMaterializer>().StagingRoot,
        scriptRunner);

    return harnessChat.AsHarnessAgent(new HarnessAgentOptions
    {
        Name = "HarnessAgent",
        Description = "General-purpose Colony agent running Microsoft Agent Framework's " +
                      "batteries-included harness loop (multi-turn tool use, todo planning, " +
                      "progressive skill loading) instead of the PMCR-O split-turn cycle.",
        DisableAgentSkillsProvider = true, // we supply our own, pointed at the Colony's real skills tree
        AIContextProviders = [harnessSkillsProvider],

        // ARCH-HARNESS-002 (2026-07-22): the harness previously ran once per
        // invocation, by its own default -- confirmed via reflection against
        // this repo's actual Microsoft.Agents.AI.dll (1.13.0):
        //   LoopEvaluator.EvaluateAsync(LoopContext, CancellationToken) -> LoopEvaluation
        //   LoopEvaluation.Stop() / .Continue(feedback) / .ContinueWithMessages(...)
        // CompletionMarkerLoopEvaluator (built-in, Microsoft.Agents.AI namespace)
        // re-invokes the agent until its own response contains the literal
        // marker below, per the harness instructions. This does NOT touch
        // PmcroLoop's outer retry loop or its HIL gate (see the
        // ARCH-HARNESS-001 comment above) -- this only makes the
        // already-separate, read-only HarnessAgent actually loop instead of
        // stopping after one turn, closing the gap noted in that comment.
        // MaxIterations is a hard safety cap independent of the completion
        // marker, in case the model never emits it.
        LoopEvaluators = [new CompletionMarkerLoopEvaluator("HARNESS_TASK_DONE", new CompletionMarkerLoopEvaluatorOptions())],
        LoopAgentOptions = new LoopAgentOptions { MaxIterations = 8 },

        ChatOptions = new ChatOptions
        {
            Instructions =
                """
                You are the PMCR-O Colony's harness-based agent. You have direct access to read-only tools and the authoritative get_skill_catalog tool.
                CATALOG QUESTIONS: For any question about skill count, names, plugins,
                or descriptions, call get_skill_catalog and answer only from its result.
                Never invent skill names and never present commands or tool variants as
                skills. The authoritative source is unique SKILL.md files in the MAF
                staging root.

                You have multi-turn access to read-only filesystem and terminal tools,
                plus any Colony skill you choose to load. Work step by step; you are not
                limited to one tool call.

                COMPLETION MARKER (ARCH-HARNESS-002): when the user's task is
                fully complete, end your final response with the literal text
                HARNESS_TASK_DONE on its own line. Do not emit this marker
                early -- only once no further tool calls or steps are needed.
                If the task genuinely requires more than 8 turns, say so
                explicitly instead of emitting the marker prematurely.
                """,
            Tools = [..cache.GetReadTools(), getSkillCatalogTool],
            AdditionalProperties = new() { ["think"] = (object)false },
        },
    });
});

var app = builder.Build();

// ARCH-MARKETPLACE-BRIDGE-001 (2026-07-20): Synchronous first-pass materialization
// before the keyed Orchestrator/Harness agents are resolved (their constructors
// reference MarketplaceSkillsMaterializer.StagingRoot). Subsequent hot-reloads are
// handled by MarketplaceSkillsWatcherService's IHostedService.
await app.Services.GetRequiredService<MarketplaceSkillsMaterializer>().MaterializeAsync();

// ARCH-CTRL-002 (2026-08-09): MapControllers() moved up here, ahead of the
// --run-declarative-sealed short-circuit below. Previously this call lived
// after that block (see the old comment this replaces) -- meaning the CLI
// path called Environment.Exit(0) and returned before this line, and before
// app.RunAsync(), so Kestrel never started listening and /hil/approve was
// never reachable during a --run-declarative-sealed run. Routing lives in
// Controllers/HilController.cs; that controller itself returns 404 outside
// Development, so mapping it unconditionally here is safe.
app.MapControllers();

// ARCH-DECLARATIVE-003 (2026-08-06): full-DI entry point for the declarative
// path -- unlike --validate-declarative/--run-declarative above (which
// deliberately short-circuit BEFORE DI/Ollama/MCP wiring), this flag runs
// AFTER the real host is built so DeclarativeCycleRunner gets the REAL
// ISubjectAgentRegistry (real filesystem-agent with real mcp-filesystem
// tools, real Colony-law instructions) and the REAL FileTrailWriter (writes
// under config.FileSystemRoot/.pmcro/trails, not console output). Requires
// mcp-filesystem/Ollama actually running, same as any other real cycle.
//
// ARCH-CTRL-002 (2026-08-09): added app.StartAsync()/StopAsync() around the
// runner call. Root cause confirmed live: this block used to call
// runner.RunAsync() and Environment.Exit(0) directly, with app.RunAsync()
// never reached in this branch -- Kestrel never bound a port, so /hil/approve
// (needed by DevUiHilChannel.RequestAsync while HandleType1ApprovalAsync
// blocks waiting for a human decision) was unreachable for the entire
// duration of the run, not just briefly. StartAsync() brings the host up
// (binds Kestrel, activates MapControllers() above) without blocking on
// shutdown the way RunAsync() does, so the CLI runner can proceed while the
// HIL endpoint is live; StopAsync() cleans up before Environment.Exit(0).
if (args.Contains("--run-declarative-sealed"))
{
    await app.StartAsync();

    var runner = app.Services.GetRequiredService<ProjectName.OrchestratorService.Workflows.Declarative.DeclarativeCycleRunner>();
    var seedIntentArg = "Explore the repo root and report what you find.";
    var argIdx = Array.IndexOf(args, "--run-declarative-sealed");
    if (argIdx >= 0 && argIdx + 1 < args.Length && !args[argIdx + 1].StartsWith("--"))
        seedIntentArg = args[argIdx + 1];

    var result = await runner.RunAsync(seedIntentArg);
    Console.WriteLine($"[run-declarative-sealed] Disposition={result.Disposition} Cycle={result.CycleNumber}");
    Console.WriteLine($"[run-declarative-sealed] FinalOutput={result.FinalOutput}");
    if (result.HaltReason is not null) Console.WriteLine($"[run-declarative-sealed] HaltReason={result.HaltReason}");

    await app.StopAsync();
    Environment.Exit(0);
}

app.MapGrpcService<ProjectName.OrchestratorService.Services.OrchestratorService>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenAIResponses();
    app.MapOpenAIConversations();
    app.MapDevUI();
}

// ARCH-AGUI-001 (2026-07-11): exposes the keyed "Orchestrator" AIAgent over the
// real AG-UI protocol (SSE-based run_started/text_message_*/run_finished events)
// for CopilotKit's runtime to consume directly via an HttpAgent, no OpenAI-shim
// needed. Mapped unconditionally (not gated to Development) since this is the
// production-facing agent surface, unlike DevUI. If a frontend is added under
// src/ later, point its CopilotKit runtime (or @ag-ui/client HttpAgent) at this
// service's base URL + "/agui".
//
// ARCH-AGUI-STATE-001 (2026-07-13): wrapped in PmcroStateBridgeAgent so PMCR-O
// phase transitions (Planning/Checking/Reflecting/CycleComplete/Sealed) stream
// out as AG-UI STATE_SNAPSHOT events, not just the final run_pmcro_cycle result.
// See Services/PmcroStateBridgeAgent.cs for the mechanism and the one flagged
// build-verification risk (AgentSession vs AgentThread on this repo's pinned
// Microsoft.Agents.AI 1.13.0).
var orchestratorAgent = app.Services.GetRequiredKeyedService<AIAgent>("Orchestrator");
app.MapAGUIServer("/agui", new PmcroStateBridgeAgent(orchestratorAgent));

// ARCH-HARNESS-001: independent AG-UI surface for the harness agent, parallel
// to "/agui" above. Not wrapped in PmcroStateBridgeAgent -- that bridge exists
// to surface PMCR-O's own Planning/Checking/Reflecting phase transitions,
// which don't apply to the harness's own internal loop.
// RENAME (2026-07-22, 1.14.0 upgrade): MapAGUI() -> MapAGUIServer().
var harnessAgent = app.Services.GetRequiredKeyedService<AIAgent>("HarnessAgent");
app.MapAGUIServer("/agui/harness", harnessAgent);

app.MapDefaultEndpoints();
await app.RunAsync();