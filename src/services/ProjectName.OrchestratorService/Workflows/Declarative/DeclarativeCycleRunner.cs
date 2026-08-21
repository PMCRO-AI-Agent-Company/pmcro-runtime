// Workflows/Declarative/DeclarativeCycleRunner.cs
// ARCH-DECLARATIVE-003 (2026-08-06): first real integration of the declarative
// (DeclarativeWorkflowBuilder-parsed YAML) PMCR-O path into the SAME sealed-trail
// mechanism PmcroCycleWorkflow/FileTrailWriter already use for the hand-rolled
// WorkflowBuilder path. Prior state (see ValidationHarness.cs) only proved the
// YAML parses and runs against real Ollama-backed agents via console output --
// it never touched ITrailWriter, so no GUID trail folder / disposition.json
// existed for it. This class is what actually calls trailWriter.WriteAsync per
// cycle and trailWriter.SealAsync at the end, using the SAME LoopFrameBuilders
// parsers PmcroCycleWorkflow uses, so the two paths produce byte-for-byte
// comparable trail artifacts.
//
// SCOPE (deliberately not yet general): subjectAgent is fixed to whatever
// pattern-a-macro-cycle.yaml's Local.subjectAgent SetVariable currently says
// (filesystem-agent, per the ARCH-DECLARATIVE-001 fix). Making this configurable
// per-run would require either per-run YAML templating or a verified
// structured-TInput/inputTransform mechanism -- neither is proven yet, so this
// class does not invent one. Passing a different subjectAgentName here only
// changes which trail folder FileTrailWriter namespaces under and which frame
// metadata is recorded; it does NOT change which agent the YAML actually
// invokes at invoke_subject (still whatever Local.subjectAgent is hardcoded to).
using System.Text;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Declarative;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProjectName.OrchestratorService.Configuration;
using ProjectName.OrchestratorService.Loop;
using ProjectName.OrchestratorService.Services;

namespace ProjectName.OrchestratorService.Workflows.Declarative;

public sealed class DeclarativeCycleRunner(
    IChatClient chatClient,
    ISubjectAgentRegistry realSubjectAgents,
    ITrailWriter trailWriter,
    McpToolCache mcpToolCache,
    IHilChannel hilChannel,
    IOptions<OrchestratorConfig> config,
    ILogger<DeclarativeCycleRunner> logger)
{
    private const string PlannerInstructionsSchema = """
        Output schema: {
          "intent_summary": "...", "assumptions": [], "resource_assumptions": [{ "would_need": "...", "why": "...", "fallback": "human_relay" }],
          "step": { "step_id": 1, "action": "...", "inputs": [], "outputs": [], "agent_or_tool": "..." }, "success_criteria": "...",
          "instruction_for_subject": "one plain-language sentence telling the subject agent exactly which tool to call and with what argument, e.g. 'Call ListDirectory on /the/real/path.'"
        }
        HARD CONSTRAINT: plan EXACTLY ONE atomic action per cycle. "action" MUST be one of the VERIFIED_RESOURCES tool names.
        instruction_for_subject MUST name the real tool and the real argument value verbatim (no placeholders like "the target path") -- the subject agent only sees this one field, not the rest of the JSON.
        Output valid JSON only.
        """;

    private const string CheckerInstructionsSchema = """
        Output schema: { "verdict": "PASS | PARTIAL | FAIL", "criteria_results": [{ "criterion": "...", "result": "PASS | FAIL", "rationale": "..." }],
          "findings": [{ "severity": "INFO | WARNING | ERROR", "finding": "..." }], "recommendation": "..." }
        Output valid JSON only.
        """;

    private const string ReflectorInstructionsSchema = """
        Output schema: { "final_output": "...", "signal": "GOAL_COMPLETED | RETRY | HALT",
          "improvements": [{ "area": "...", "suggestion": "..." }],
          "next_seed_intent": "... | null" }
        next_seed_intent null on RETRY; self-contained on GOAL_COMPLETED. Output valid JSON only.
        """;

    public async Task<PmcroResult> RunAsync(
        string seedIntent, string subjectAgentName = "filesystem-agent",
        string project = "project1", CancellationToken ct = default)
    {
        var trailId = Guid.NewGuid().ToString();
        var yamlPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..",
            "Workflows", "Declarative", "pattern-a-macro-cycle.yaml");
        yamlPath = Path.GetFullPath(yamlPath);

        var subjectAgentInstance = realSubjectAgents.Resolve(subjectAgentName)
            ?? throw new InvalidOperationException(
                $"No real subject agent registered under '{subjectAgentName}'. " +
                "DeclarativeCycleRunner reuses the production ISubjectAgentRegistry, not a stub.");

        var localRegistry = new SubjectAgentRegistry();
        // PLAN-002 grounding (matches PmcroCycleWorkflow.BuildMakerTurn): without
        // this, qwen3:8b's Planner invents actions with no backing tool (the
        // 2026-07-03 playwright pilot bug) -- confirmed as the actual cause of
        // this runner's first sealed trail (f6d95cec...) planning a generic
        // "execute_task"/"specific_tool" step instead of a real ListDirectory.
        var plannerInstr = PlannerInstructionsSchema +
            $"\n\nVERIFIED_RESOURCES for subject agent '{subjectAgentName}':\n{mcpToolCache.GetVerifiedResourcesJson(subjectAgentName)}";
        localRegistry.Register("PlannerAgent", chatClient.AsAIAgent(new ChatClientAgentOptions
        {
            Name = "PlannerAgent",
            ChatOptions = new ChatOptions { Instructions = plannerInstr, AdditionalProperties = new() { ["think"] = (object)false } }
        }));
        localRegistry.Register("CheckerAgent", chatClient.AsAIAgent(new ChatClientAgentOptions
        {
            Name = "CheckerAgent",
            ChatOptions = new ChatOptions { Instructions = CheckerInstructionsSchema, AdditionalProperties = new() { ["think"] = (object)false } }
        }));
        localRegistry.Register("ReflectorAgent", chatClient.AsAIAgent(new ChatClientAgentOptions
        {
            Name = "ReflectorAgent",
            ChatOptions = new ChatOptions { Instructions = ReflectorInstructionsSchema, AdditionalProperties = new() { ["think"] = (object)false } }
        }));
        // Real subject agent (real tools, real Colony-law instructions) under its
        // own registered name -- NOT the stub echo agent ValidationHarness used.
        localRegistry.Register(subjectAgentName, subjectAgentInstance);

        var provider = new SubjectAgentRegistryProvider(localRegistry);
        var options = new DeclarativeWorkflowOptions(provider);

        // ARCH-DECLARATIVE-011 (2026-08-07): matches PmcroCycleWorkflow.BuildCycleIntent
        // -- confirmed root cause of the Planner emitting "/path/to/repo/root" instead of
        // a real path: this runner had `config` injected (IOptions<OrchestratorConfig>)
        // but never once read config.Value anywhere in the file. The bare seedIntent went
        // straight into RunStreamingAsync with no REAL_PROJECT_ROOT grounding, unlike the
        // hand-rolled path. seedIntent itself stays unmodified for frame/trail recording
        // (LoopFrameBuilders calls below still use the pure seed, same as PmcroCycleWorkflow
        // records cycleIntent separately from the seedIntent it passes to BuildPlannerFrame);
        // only the string actually fed to the workflow run is grounded.
        var groundedSeedIntent =
            $"INTENT: {seedIntent}\nPROJECT: {project}\n" +
            $"REAL_PROJECT_ROOT (use this exact path): {config.Value.FileSystemRoot}\n" +
            $"SUBJECT_AGENT: {subjectAgentName}";

        Workflow workflow;
        try
        {
            workflow = DeclarativeWorkflowBuilder.Build<string>(yamlPath, options);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Declarative] Build FAILED for trail {Trail}", trailId);
            return await SealHaltAsync(trailId, subjectAgentName, seedIntent, project, 1, $"Build failed: {ex.Message}");
        }

        var bufferKeys = new[] { "invoke_planner", "invoke_subject", "invoke_checker", "invoke_reflector" };
        var buffers = bufferKeys.ToDictionary(k => k, _ => new StringBuilder(), StringComparer.OrdinalIgnoreCase);
        var cycle = 1;
        var seenFirstCycleStart = false;
        ReflectorFrame? lastReflectorFrame = null;

        async Task<ReflectorFrame> WriteCycleTrailAsync(int cycleNum)
        {
            var rawPlan = buffers["invoke_planner"].ToString();
            var rawArtifact = buffers["invoke_subject"].ToString();
            // ARCH-DECLARATIVE-004 (2026-08-06): same fix as PmcroLoop.cs/
            // PmcroCycleWorkflow.cs -- the real filesystem-agent responds in
            // plain natural language after a native ReadFile call (confirmed via
            // trail 49fc3f09..., which had StepResults:[] despite ReadFile
            // genuinely being planned). Synthesize from real McpToolCache capture
            // data whenever the raw text isn't already execution_report JSON.
            if (string.IsNullOrWhiteSpace(rawArtifact) || !McpToolCache.HasExecutionReport(rawArtifact))
                rawArtifact = mcpToolCache.SynthesizeArtifact(subjectAgentName);
            var rawCheck = buffers["invoke_checker"].ToString();
            var rawReflection = buffers["invoke_reflector"].ToString();
            var plannerFrame = LoopFrameBuilders.BuildPlannerFrame(rawPlan, seedIntent, project, trailId, cycleNum, subjectAgentName);
            var makerFrame = LoopFrameBuilders.BuildMakerFrame(rawArtifact, plannerFrame, seedIntent, trailId);
            var checkerFrame = LoopFrameBuilders.BuildCheckerFrame(rawCheck, makerFrame, seedIntent, trailId);
            var reflectorFrame = LoopFrameBuilders.BuildReflectorFrame(rawReflection, rawReflection, seedIntent, trailId, cycleNum);
            await trailWriter.WriteAsync(subjectAgentName, trailId, seedIntent, cycleNum, plannerFrame, makerFrame, checkerFrame, reflectorFrame);
            logger.LogInformation("[Declarative] Wrote cycle {Cycle} trail for {Trail}", cycleNum, trailId);
            return reflectorFrame;
        }

        try
        {
            // ARCH-DECLARATIVE-007 (2026-08-07): removed the follow-up
            // TrySendMessageAsync(new TurnToken(...)) that used to run here.
            // RunStreamingAsync(workflow, seedIntent) already delivers seedIntent
            // as the run's input and starts execution -- confirmed against the
            // official quickstart (Microsoft Learn, "Your First Declarative
            // Workflow"), which calls RunStreamingAsync(workflow, input,
            // checkpointManager) and goes straight to WatchStreamAsync() with no
            // TurnToken at all, and gets System.LastMessage.Text populated
            // correctly (documented output: "Activity: Hello, Alice!" for
            // input "Alice"). TurnToken belongs to the OpenStreamingAsync
            // pattern (open with no input, then explicitly send messages incl.
            // a turn signal to kick off processing) -- mixing it into the
            // RunStreamingAsync path meant a second, textless message landed
            // immediately after the real seed message, which is the leading
            // suspect for why System.LastMessage.Text was consistently
            // generic/empty across every cycle regardless of seed content
            // (including the ZQXJ77... marker-string probe). If removing this
            // breaks event flushing (i.e. WatchStreamAsync never completes),
            // that's a different, real finding -- not a reason to silently
            // restore this line without investigating further.
            StreamingRun run = await InProcessExecution.RunStreamingAsync(workflow, groundedSeedIntent);

            await foreach (WorkflowEvent evt in run.WatchStreamAsync().WithCancellation(ct))
            {
                if (evt is ExecutorFailedEvent or WorkflowErrorEvent)
                {
                    var exObj = evt switch { ExecutorFailedEvent f => (object?)f.Data, WorkflowErrorEvent w => w.Data, _ => null };
                    var msg = (exObj as Exception)?.Message ?? "unknown workflow error";
                    logger.LogError("[Declarative] {EventType}: {Message}", evt.GetType().Name, msg);
                    if (buffers.Any(b => b.Value.Length > 0)) await WriteCycleTrailAsync(cycle);
                    return await SealHaltAsync(trailId, subjectAgentName, seedIntent, project, cycle, $"{evt.GetType().Name}: {msg}");
                }

                // ARCH-DECLARATIVE-005 (2026-08-06): real MAF-native HIL gate.
                // Verified via web search + reflection (--introspect-hil-events)
                // against the actual Microsoft.Agents.AI.Workflows.dll rather than
                // assumed: RequestExternalInput (hil_approve_type1 in the YAML)
                // pauses the workflow and emits RequestInfoEvent; the response is
                // sent back via run.SendResponseAsync(evt.Request.CreateResponse(...)).
                // This is the ONLY RequestExternalInput in pattern-a-macro-cycle.yaml,
                // so no port disambiguation is needed. The actual approval decision
                // still goes through the SAME IHilChannel/DevUiHilChannel every other
                // path uses -- Colony Law (HilChannel.cs DEV-GODMODE-001) requires
                // real human-in-the-loop gating here, never auto-approval.
                if (evt is RequestInfoEvent reqEvt)
                {
                    var approved = await HandleType1ApprovalAsync(trailId, cycle, subjectAgentName, buffers["invoke_subject"], ct);
                    // ARCH-DECLARATIVE-017 (2026-08-09): CreateResponse(string) throws --
                    // confirmed live ("Message type String does not match expected response
                    // type ...ExternalInputResponse of input port hil_approve_type1_Input")
                    // the moment the HIL gate actually fired for the first time (see
                    // ARCH-DECLARATIVE-016). Verified via the real package XML docs
                    // (Microsoft.Agents.AI.Workflows.Declarative.xml, 1.17.0):
                    // ExternalInputResponse's only constructors take a ChatMessage or
                    // IList<ChatMessage>, not a bare string -- RequestExternalInput expects
                    // its response wrapped the same way every other turn's input is
                    // (UserMessage(...) elsewhere in this file), not a raw CreateResponse(string)
                    // overload (that overload exists on other request kinds, not this one).
                    var responseMessage = new Microsoft.Extensions.AI.ChatMessage(
                        Microsoft.Extensions.AI.ChatRole.User, approved ? "approved" : "denied");
                    await run.SendResponseAsync(reqEvt.Request.CreateResponse(
                        new Microsoft.Agents.AI.Workflows.Declarative.Events.ExternalInputResponse(responseMessage)));
                    continue;
                }

                // ARCH-DECLARATIVE-003: verified via --introspect-workflow-events
                // (2026-08-06) -- ExecutorInvokedEvent/AgentResponseUpdateEvent/
                // AgentResponseEvent all carry their own ExecutorId directly, so
                // routing doesn't need a "currentExecutor" tracking hack at all.
                if (evt is ExecutorInvokedEvent inv && inv.ExecutorId == "cycle_start")
                {
                    if (seenFirstCycleStart)
                    {
                        lastReflectorFrame = await WriteCycleTrailAsync(cycle);
                        foreach (var sb in buffers.Values) sb.Clear();
                        cycle++;
                    }
                    seenFirstCycleStart = true;
                    continue;
                }

                switch (evt)
                {
                    // ARCH-DECLARATIVE-007 (2026-08-07) TEMP DEBUG: MessageActivityEvent
                    // (what SendActivity emits) was previously unhandled here -- the
                    // debug_dump_lastmessage probe in the YAML has been firing this whole
                    // time but its output was silently swallowed, never logged anywhere.
                    // Every prior conclusion about System.LastMessage.Text was inferred
                    // from Planner output, never observed directly. Remove after use.
                    case MessageActivityEvent m:
                        logger.LogWarning("[Declarative][DEBUG] MessageActivityEvent: {Text}", m.Message);
                        break;
                    case AgentResponseUpdateEvent u when u.ExecutorId is not null && buffers.TryGetValue(u.ExecutorId, out var ubuf) && u.Data is not null:
                        ubuf.Append(u.Data.ToString());
                        break;
                    case AgentResponseEvent r when r.ExecutorId is not null && buffers.TryGetValue(r.ExecutorId, out var rbuf) && rbuf.Length == 0 && r.Data is not null:
                        rbuf.Append(r.Data.ToString());
                        break;
                }
            }

            // Terminal cycle (no further cycle_start to trigger the write) --
            // finalize whatever the last cycle accumulated.
            lastReflectorFrame = await WriteCycleTrailAsync(cycle);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "[Declarative] RunAsync failed for trail {Trail}", trailId);
            if (buffers.Any(b => b.Value.Length > 0)) await WriteCycleTrailAsync(cycle);
            return await SealHaltAsync(trailId, subjectAgentName, seedIntent, project, cycle, $"Exception: {ex.Message}");
        }

        var finalDisposition = lastReflectorFrame!.Disposition switch
        {
            LoopDisposition.Accept => Disposition.Accept,
            _ => Disposition.Halt
        };
        var result = new PmcroResult(
            finalDisposition, lastReflectorFrame.CycleNumber, lastReflectorFrame.FinalOutput,
            finalDisposition == Disposition.Halt ? (lastReflectorFrame.HaltReason ?? "Declarative cycle ended without GOAL_COMPLETED") : null,
            seedIntent, NextSeedIntent: lastReflectorFrame.NextSeedIntent);
        await trailWriter.SealAsync(subjectAgentName, trailId, result);
        logger.LogInformation("[Declarative] Sealed trail {Trail} — {Disposition}", trailId, finalDisposition);
        return result;
    }

    private async Task<PmcroResult> SealHaltAsync(
        string trailId, string subjectAgentName, string seedIntent, string project, int cycle, string reason)
    {
        var result = new PmcroResult(Disposition.Halt, cycle, "", reason, seedIntent);
        await trailWriter.SealAsync(subjectAgentName, trailId, result);
        logger.LogWarning("[Declarative] Sealed HALT trail {Trail}: {Reason}", trailId, reason);
        return result;
    }

    /// <summary>
    /// ARCH-DECLARATIVE-005: mirrors PmcroCycleWorkflow.DispatchType1, triggered
    /// from the native RequestInfoEvent (hil_approve_type1) instead of a manual
    /// rawArtifact.Contains("TYPE1_PENDING") check before the next turn. Parses
    /// the type1_pending stub out of the subject agent's raw buffer, requests
    /// REAL human approval via IHilChannel, executes the real MCP write if
    /// approved, and rewrites the buffer in place so the Checker/Reflector turns
    /// (and BuildMakerFrame) see the real result instead of the pending stub.
    /// SCOPE: only WriteFile/filesystem-agent is wired -- this runner is fixed to
    /// filesystem-agent per ARCH-DECLARATIVE-003's documented scope limit, so
    /// terminal-agent/playwright-agent TYPE1 actions are out of scope here.
    /// </summary>
    private async Task<bool> HandleType1ApprovalAsync(
        string trailId, int cycle, string subjectAgentName, StringBuilder subjectBuffer, CancellationToken ct)
    {
        var raw = subjectBuffer.ToString();
        try
        {
            using var doc = JsonDocument.Parse(LoopFrameBuilders.ExtractJson(raw));
            string? innerJson = null;
            if (doc.RootElement.TryGetProperty("execution_report", out var rep) && rep.TryGetProperty("tool_calls", out var calls))
                foreach (var c in calls.EnumerateArray())
                    if (c.TryGetProperty("result", out var r) && r.GetString()?.Contains("type1_pending") == true)
                    { innerJson = r.GetString(); break; }
            if (innerJson is null && doc.RootElement.TryGetProperty("artifact", out var af)) innerJson = af.GetString();
            if (innerJson is null || !innerJson.Contains("type1_pending"))
            {
                logger.LogWarning("[Declarative] RequestInfoEvent fired but no type1_pending stub found in subject buffer -- trail={Trail} cycle={Cycle}", trailId, cycle);
                return false;
            }

            using var inner = JsonDocument.Parse(innerJson);
            var pending = inner.RootElement.GetProperty("type1_pending");
            var tool = pending.GetProperty("tool").GetString()!;
            var action = pending.GetProperty("requested_action");

            var approved = await hilChannel.RequestAsync(Guid.NewGuid().ToString("N")[..8], tool, action.ToString(), trailId, ct);
            if (!approved)
            {
                subjectBuffer.Clear();
                subjectBuffer.Append(WrapDispatch(tool, "{\"success\":false,\"error\":\"HIL_DENIED\"}"));
                return false;
            }

            var result = (subjectAgentName, tool) switch
            {
                ("filesystem-agent", "WriteFile") =>
                    await mcpToolCache.FilesystemExecuteWriteFile(action.GetProperty("path").GetString()!, action.GetProperty("content").GetString() ?? ""),
                _ => "{\"error\":\"unknown agent/tool for declarative TYPE1 dispatch\"}"
            };
            mcpToolCache.DrainCapturedResults(subjectAgentName);
            subjectBuffer.Clear();
            subjectBuffer.Append(WrapDispatch(tool, result));
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Declarative] TYPE1 dispatch failed -- trail={Trail} cycle={Cycle}", trailId, cycle);
            return false;
        }
    }

    private static string WrapDispatch(string tool, string result) =>
        $"{{\"artifact_type\":\"dispatched\",\"artifact\":\"\",\"execution_report\":{{\"steps_executed\":1,\"tool_calls\":[{{\"step_id\":1,\"tool\":\"{tool}\",\"result\":{result}}}]}}}}";
}
