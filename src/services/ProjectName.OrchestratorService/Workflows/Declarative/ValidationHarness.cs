// ARCH-DECLARATIVE-001 load-test harness. Verified real signature via
// DeclarativeWorkflowBuilder.xml (2026-08-06):
//   Build<TInput>(string workflowFile, DeclarativeWorkflowOptions options,
//                 Func<TInput, ChatMessage>? inputTransform = null) -> Workflow
//   DeclarativeWorkflowOptions(ResponseAgentProvider agentProvider)
//
// Purpose: parse pattern-a-macro-cycle.yaml through the REAL parser and report
// pass/fail with the actual exception, before wiring this into Program.cs's
// live request path. No agents need to be registered to validate parse/build —
// AgentProvider is only invoked at run time (Run.SendMessageAsync), not at
// Build() time.
using System.Text;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Declarative;
using Microsoft.Extensions.AI;
using OllamaSharp;
using ProjectName.OrchestratorService.Services;

namespace ProjectName.OrchestratorService.Workflows.Declarative;

public static class ValidationHarness
{
    public static int ValidatePatternA()
    {
        var yamlPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..",
            "Workflows", "Declarative", "pattern-a-macro-cycle.yaml");
        yamlPath = Path.GetFullPath(yamlPath);

        Console.WriteLine($"[validate-declarative] YAML path: {yamlPath}");
        Console.WriteLine($"[validate-declarative] Exists: {File.Exists(yamlPath)}");

        var registry = new SubjectAgentRegistry();
        var provider = new SubjectAgentRegistryProvider(registry);
        var options = new DeclarativeWorkflowOptions(provider);

        try
        {
            var workflow = DeclarativeWorkflowBuilder.Build<string>(yamlPath, options);
            Console.WriteLine("[validate-declarative] SUCCESS - workflow built.");
            Console.WriteLine($"[validate-declarative] Workflow type: {workflow.GetType().FullName}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("[validate-declarative] FAILED to build workflow.");
            Console.WriteLine($"[validate-declarative] {ex.GetType().FullName}: {ex.Message}");
            if (ex.InnerException is not null)
                Console.WriteLine($"[validate-declarative] Inner: {ex.InnerException.GetType().FullName}: {ex.InnerException.Message}");
            Console.WriteLine(ex.StackTrace);
            return 1;
        }
    }

    // ── Real run: registers PlannerAgent/CheckerAgent/ReflectorAgent (real
    // Ollama-backed AIAgents, same schemas PmcroLoop.cs's hand-rolled path uses)
    // plus filesystem-agent as the subjectAgent, then actually invokes the built
    // Workflow via InProcessExecution -- the layer Build() alone can't test.
    public static async Task<int> RunPatternA()
    {
        var yamlPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "Workflows", "Declarative", "pattern-a-macro-cycle.yaml"));
        Console.WriteLine($"[run-declarative] YAML path: {yamlPath}");

        IChatClient rawClient = new OllamaApiClient(new Uri("http://localhost:11434")) { SelectedModel = "qwen3:8b" };
        var chat = rawClient.AsBuilder().UseFunctionInvocation(configure: c => c.MaximumIterationsPerRequest = 1).Build();

        var registry = new SubjectAgentRegistry();

        registry.Register("PlannerAgent", chat.AsAIAgent(new ChatClientAgentOptions
        {
            Name = "PlannerAgent",
            ChatOptions = new ChatOptions
            {
                // SYNCED (2026-08-07, ARCH-DECLARATIVE-013) with
                // DeclarativeCycleRunner.PlannerInstructionsSchema -- this fixture
                // had drifted (no instruction_for_subject field, no grounding),
                // which meant --run-declarative was silently NOT exercising the
                // same extraction-logic bug --run-declarative-sealed hits. Kept
                // deliberately in sync going forward: this harness's whole value
                // is being a faithful, richer-diagnostics stand-in for the real
                // path (full exception unwind + stack trace via DumpException,
                // vs DeclarativeCycleRunner's .Message-only logging).
                Instructions = """
                    You are the Planner. Plan exactly ONE atomic filesystem action.
                    Output schema:
                    { "step": { "step_id": 1, "action": "ListDirectory", "inputs": [], "agent_or_tool": "filesystem-agent" }, "success_criteria": "...",
                      "instruction_for_subject": "one plain-language sentence telling the subject agent exactly which tool to call and with what argument, e.g. 'Call ListDirectory on /the/real/path.'" }
                    instruction_for_subject MUST name the real tool and a real-looking argument verbatim (no placeholders).
                    Output valid JSON only.
                    """,
                AdditionalProperties = new() { ["think"] = (object)false },
            },
        }));

        registry.Register("CheckerAgent", chat.AsAIAgent(new ChatClientAgentOptions
        {
            Name = "CheckerAgent",
            ChatOptions = new ChatOptions
            {
                Instructions = """
                    You are the Checker. Output schema:
                    { "verdict": "PASS | PARTIAL | FAIL", "criteria_results": [], "findings": [] }
                    Output valid JSON only.
                    """,
                AdditionalProperties = new() { ["think"] = (object)false },
            },
        }));

        registry.Register("ReflectorAgent", chat.AsAIAgent(new ChatClientAgentOptions
        {
            Name = "ReflectorAgent",
            ChatOptions = new ChatOptions
            {
                // NOTE: real PmcroLoop.cs schema uses "signal": "GOAL_COMPLETED|RETRY|HALT",
                // NOT "disposition". pattern-a-macro-cycle.yaml's GotoAction condition
                // checks Local.ReflectorResult.disposition = "Retry" -- a real field-name
                // mismatch this run is expected to surface, not paper over.
                Instructions = """
                    You are the Reflector. Output schema:
                    { "final_output": "...", "signal": "GOAL_COMPLETED | RETRY | HALT", "improvements": [] }
                    Output valid JSON only.
                    """,
                AdditionalProperties = new() { ["think"] = (object)false },
            },
        }));

        registry.Register("filesystem-agent", chat.AsAIAgent(new ChatClientAgentOptions
        {
            Name = "filesystem-agent",
            ChatOptions = new ChatOptions
            {
                Instructions = "You are a stub filesystem-agent for a declarative-workflow load test. Output: {\"artifact_type\":\"other\",\"artifact\":\"stub\",\"execution_report\":{\"steps_executed\":1,\"tool_calls\":[]}}",
                AdditionalProperties = new() { ["think"] = (object)false },
            },
        }));

        var provider = new SubjectAgentRegistryProvider(registry);
        var options = new DeclarativeWorkflowOptions(provider);

        Workflow workflow;
        try
        {
            workflow = DeclarativeWorkflowBuilder.Build<string>(yamlPath, options);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[run-declarative] Build FAILED: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
        Console.WriteLine("[run-declarative] Build OK. Starting run...");

        try
        {
            // SYNCED (2026-08-07, ARCH-DECLARATIVE-013) with DeclarativeCycleRunner's
            // REAL_PROJECT_ROOT grounding -- ungrounded here previously, meaning the
            // Planner (now that its instructions above ask for instruction_for_subject
            // with "no placeholders") had no real path to name. W:\PMCR_O\PMCR-O-Marketplace
            // hardcoded rather than read from OrchestratorConfig since this harness
            // deliberately runs before any DI/config wiring (see class header comment).
            var groundedSeed =
                "INTENT: Explore the repo root and report what you find.\n" +
                "PROJECT: project1\n" +
                "REAL_PROJECT_ROOT (use this exact path): W:\\PMCR_O\\PMCR-O-Marketplace\n" +
                "SUBJECT_AGENT: filesystem-agent";
            StreamingRun run = await InProcessExecution.RunStreamingAsync(workflow, groundedSeed);
            await run.TrySendMessageAsync(new TurnToken(emitEvents: true));

            var sawAny = false;
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            await foreach (WorkflowEvent evt in run.WatchStreamAsync().WithCancellation(cts.Token))
            {
                sawAny = true;
                if (evt is ExecutorFailedEvent or WorkflowErrorEvent)
                {
                    Console.WriteLine($"[run-declarative] EVENT: {evt.GetType().Name} (FULL DUMP FOLLOWS)");
                    var exObj = evt switch
                    {
                        ExecutorFailedEvent f => (object?)f.Data,
                        WorkflowErrorEvent w => w.Data,
                        _ => null
                    };
                    DumpException(exObj as Exception);
                }
                else
                {
                    Console.WriteLine($"[run-declarative] EVENT: {evt.GetType().Name} -> {Truncate(evt.ToString(), 300)}");
                }
            }
            Console.WriteLine(sawAny ? "[run-declarative] Stream completed." : "[run-declarative] Stream produced NO events.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[run-declarative] RUN FAILED: {ex.GetType().FullName}: {ex.Message}");
            if (ex.InnerException is not null)
                Console.WriteLine($"[run-declarative] Inner: {ex.InnerException.GetType().FullName}: {ex.InnerException.Message}");
            Console.WriteLine(ex.StackTrace);
            return 1;
        }
    }

    private static string Truncate(string? s, int max) =>
        s is null ? "" : s.Length <= max ? s : s[..max] + "...";

    private static void DumpException(Exception? ex, int depth = 0)
    {
        if (ex is null) return;
        var pad = new string(' ', depth * 2);
        Console.WriteLine($"{pad}[run-declarative] {ex.GetType().FullName}: {ex.Message}");
        if (ex is AggregateException agg)
        {
            foreach (var inner in agg.InnerExceptions) DumpException(inner, depth + 1);
        }
        else if (ex.InnerException is not null)
        {
            DumpException(ex.InnerException, depth + 1);
        }
    }
}
