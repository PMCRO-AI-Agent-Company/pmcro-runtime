// Workflows/PmcroCycleWorkflow.cs
// MAF-native WorkflowBuilder execution graph replacing the hand-rolled PmcroLoop.
// Inlines frame-building and instruction-composition logic (those are private on
// PmcroLoop), delegating dispatch/type-classification to McpToolCache.

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProjectName.OrchestratorService.Configuration;
using ProjectName.OrchestratorService.Loop;
using ProjectName.OrchestratorService.Services;
using ProjectName.OrchestratorService.Skills;
using ProjectName.OrchestratorService.Workflows.Gates;

namespace ProjectName.OrchestratorService.Workflows;

public sealed class PmcroCycleWorkflow(
    IChatClient chatClient,
    McpToolCache mcpToolCache,
    IHilChannel hilChannel,
    ITrailWriter trailWriter,
    IOptions<OrchestratorConfig> config,
    SkillManifestReader skillManifestReader,
    ILogger<PmcroCycleWorkflow> logger)
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<PmcroResult> RunAsync(
        string seedIntent, string trailId, string project,
        string subjectAgentName, AIAgent subjectAgentInstance, CancellationToken ct = default)
    {
        var maxCycles = config.Value.MaxLoops;
        var executedActions = new List<string>();
        var cumulativeEvidence = new List<CumulativeEvidenceEntry>();
        var retryContext = string.Empty;
        PlannerFrame? plannerFrame = null;
        MakerFrame? makerFrame = null;
        CheckerFrame? checkerFrame = null;
        ReflectorFrame? reflectorFrame = null;

        for (int cycle = 1; cycle <= maxCycles; cycle++)
        {
            logger.LogInformation("[PMCRO-WF] Cycle {Cycle}/{Max} — trail={Trail}", cycle, maxCycles, trailId);
            PmcroStateBroadcast.Publish(new PmcroCycleStateSnapshot(trailId, cycle, "Planning"));

            var cycleIntent = BuildCycleIntent(seedIntent, project, subjectAgentName, cycle, retryContext);
            if (subjectAgentName == "terminal-agent") cycleIntent = await InjectTerminalPreflight(cycleIntent, ct);

            var eventLog = new List<string>();
            string rawPlan, rawArtifact, rawCheck, rawReflection;
            try
            {
                // ── TURN A: Planner → subject agent ────────────────────
                var (makerWorkflow, makerBuffers) =
                    BuildMakerTurn(cycle, retryContext, subjectAgentInstance, subjectAgentName, executedActions);
                var makerInput = new List<ChatMessage> { new(ChatRole.User, cycleIntent) };
                await RunWorkflowStreamAsync(makerWorkflow, makerInput, makerBuffers, eventLog, cycle, ct);
                rawPlan = makerBuffers["PlannerAgent"].ToString();
                rawArtifact = makerBuffers[subjectAgentInstance.Name ?? subjectAgentName].ToString();
                // ARCH-DECLARATIVE-004 (2026-08-06): fixed to match PmcroLoop.cs --
                // synthesize from real McpToolCache captures whenever raw text isn't
                // already execution_report.tool_calls JSON, not only when empty.
                if (string.IsNullOrWhiteSpace(rawArtifact) || !McpToolCache.HasExecutionReport(rawArtifact))
                    rawArtifact = mcpToolCache.SynthesizeArtifact(subjectAgentName);
                else mcpToolCache.DrainCapturedResults(subjectAgentName);
                if (rawArtifact.Contains("TYPE1_PENDING", StringComparison.Ordinal))
                    rawArtifact = await DispatchType1(rawArtifact, subjectAgentName, trailId, ct);

                // ── TURN B: Checker → Reflector ─────────────────────────
                PmcroStateBroadcast.Publish(new PmcroCycleStateSnapshot(trailId, cycle, "Checking"));
                var (auditWorkflow, auditBuffers) =
                    BuildAuditTurn(cycle, seedIntent, subjectAgentName, cumulativeEvidence);
                var auditInput = new List<ChatMessage>
                {
                    new(ChatRole.User, cycleIntent),
                    new(ChatRole.Assistant, rawPlan) { AuthorName = "PlannerAgent" },
                    new(ChatRole.Assistant, rawArtifact) { AuthorName = subjectAgentInstance.Name ?? subjectAgentName }
                };
                await RunWorkflowStreamAsync(auditWorkflow, auditInput, auditBuffers, eventLog, cycle, ct);
                rawCheck = auditBuffers["CheckerAgent"].ToString();
                rawReflection = auditBuffers["ReflectorAgent"].ToString();
                PmcroStateBroadcast.Publish(new PmcroCycleStateSnapshot(trailId, cycle, "Reflecting"));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "[PMCRO-WF] Workflow error — cycle {Cycle}", cycle);
                return await HaltResult(trailId, subjectAgentName, seedIntent, cycle, plannerFrame, $"Workflow exception: {ex.Message}");
            }

            // ── Materialise and gate ───────────────────────────────────
            // ARCH-DECLARATIVE-002 (2026-08-06): delegates to LoopFrameBuilders,
            // shared with Workflows/Declarative/DeclarativeCycleRunner.cs, instead
            // of the private copies that used to live here.
            plannerFrame = LoopFrameBuilders.BuildPlannerFrame(rawPlan, seedIntent, project, trailId, cycle, subjectAgentName);
            var g1 = GateRunner.PlannerGate(plannerFrame, logger, cycle);
            if (!g1.Passed) return await HaltResult(trailId, subjectAgentName, seedIntent, cycle, plannerFrame, $"Gate 1: {string.Join("; ", g1.Findings)}");

            makerFrame = LoopFrameBuilders.BuildMakerFrame(rawArtifact, plannerFrame, seedIntent, trailId);
            var g2 = GateRunner.MakerGate(makerFrame, logger, cycle);
            if (!g2.Passed) return await HaltResult(trailId, subjectAgentName, seedIntent, cycle, plannerFrame, $"Gate 2: {string.Join("; ", g2.Findings)}", makerFrame, null);

            checkerFrame = LoopFrameBuilders.BuildCheckerFrame(rawCheck, makerFrame, seedIntent, trailId);
            var g3 = GateRunner.CheckerGate(checkerFrame, plannerFrame, logger, cycle);
            if (!g3.Passed) return await HaltResult(trailId, subjectAgentName, seedIntent, cycle, plannerFrame, $"Gate 3: {string.Join("; ", g3.Findings)}", makerFrame, checkerFrame);

            reflectorFrame = LoopFrameBuilders.BuildReflectorFrame(rawReflection, rawReflection, seedIntent, trailId, cycle);
            var g4 = GateRunner.ReflectorGate(reflectorFrame, logger, cycle);
            if (!g4.Passed) reflectorFrame = reflectorFrame with { HaltReason = $"Gate 4: {string.Join("; ", g4.Findings)}" };

            var action = plannerFrame.Steps.FirstOrDefault()?.Action;
            if (!string.IsNullOrWhiteSpace(action) && !executedActions.Contains(action)) executedActions.Add(action);
            cumulativeEvidence.Add(new(cycle, action ?? "", plannerFrame.SuccessCriteria ?? "", checkerFrame.AllPassed));
            await trailWriter.WriteAsync(subjectAgentName, trailId, seedIntent, cycle, plannerFrame, makerFrame, checkerFrame, reflectorFrame);
            PmcroStateBroadcast.Publish(new PmcroCycleStateSnapshot(trailId, cycle, "CycleComplete", LastAction: action, Disposition: reflectorFrame.Disposition.ToString(), AllPassed: checkerFrame.AllPassed));

            if (reflectorFrame.Disposition != LoopDisposition.Retry) break;
            retryContext = reflectorFrame.RetryContext ?? string.Empty;
        }

        var final = reflectorFrame!.Disposition switch { LoopDisposition.Accept => Disposition.Accept, _ => Disposition.Halt };
        var result = new PmcroResult(final, reflectorFrame.CycleNumber, reflectorFrame.FinalOutput,
            final == Disposition.Halt ? (reflectorFrame.HaltReason ?? "MaxLoops") : null, seedIntent, NextSeedIntent: reflectorFrame.NextSeedIntent);
        await trailWriter.SealAsync(subjectAgentName, trailId, result);
        PmcroStateBroadcast.Publish(new PmcroCycleStateSnapshot(trailId, reflectorFrame.CycleNumber, "Sealed", Disposition: final.ToString()));
        return result;
    }

    // ── Turn builders ─────────────────────────────────────────────────

    private (Workflow, Dictionary<string, StringBuilder>) BuildMakerTurn(
        int cycle, string retryContext, AIAgent subject, string subjectName, List<string> executed)
    {
        var instr = PlannerInstructionsSchema;
        var vres = mcpToolCache.GetVerifiedResourcesJson(subjectName);
        instr += $"\n\nVERIFIED_RESOURCES for subject agent '{subjectName}':\n{vres}";
        if (executed.Count > 0)
            instr += $"\n\nALREADY_EXECUTED: {JsonSerializer.Serialize(executed)}\nHARD CONSTRAINT: do not repeat these actions.";
        if (!string.IsNullOrWhiteSpace(retryContext))
            instr += $"\n\nRETRY_CONTEXT: {retryContext}";

        var planner = chatClient.AsAIAgent(new ChatClientAgentOptions
        {
            Name = "PlannerAgent",
            ChatOptions = new ChatOptions { Instructions = instr, AdditionalProperties = new() { ["think"] = (object)false } }
        });
        var wf = new WorkflowBuilder(planner).WithName($"PmcroMakerTurn_{cycle}").AddEdge(planner, subject).Build();
        return (wf, new Dictionary<string, StringBuilder>(StringComparer.OrdinalIgnoreCase)
        {
            ["PlannerAgent"] = new(), [subject.Name ?? subjectName] = new()
        });
    }

    private (Workflow, Dictionary<string, StringBuilder>) BuildAuditTurn(
        int cycle, string seedIntent, string subjectName, List<CumulativeEvidenceEntry> evidence)
    {
        var cinstr = CheckerInstructionsSchema;
        var law = skillManifestReader.ReadColonyLaws(subjectName) ?? "";
        if (!string.IsNullOrWhiteSpace(law))
            cinstr += $"\n\nCOLONY LAW COMPLIANCE: subject agent '{subjectName}' Colony Laws:\n{law}\nAdd colony_law_compliance check.";
        var checker = chatClient.AsAIAgent(new ChatClientAgentOptions
        {
            Name = "CheckerAgent",
            ChatOptions = new ChatOptions { Instructions = cinstr, Tools = mcpToolCache.GetReadTools(), AdditionalProperties = new() { ["think"] = (object)false } }
        });

        var rinstr = ReflectorInstructionsSchema;
        rinstr += $"\n\nSEED_INTENT: {seedIntent}";
        if (evidence.Count > 0)
            rinstr += $"\nCUMULATIVE_EVIDENCE: {JsonSerializer.Serialize(evidence.Select(e => new { e.Cycle, e.Action, e.SuccessCriteria, e.Passed }))}\nHARD CONSTRAINT: GOAL_COMPLETED only if CUMULATIVE_EVIDENCE saturates SEED_INTENT.";
        var reflector = chatClient.AsAIAgent(new ChatClientAgentOptions
        {
            Name = "ReflectorAgent",
            ChatOptions = new ChatOptions { Instructions = rinstr, AdditionalProperties = new() { ["think"] = (object)false } }
        });

        var wf = new WorkflowBuilder(checker).WithName($"PmcroAuditTurn_{cycle}").AddEdge(checker, reflector).Build();
        return (wf, new Dictionary<string, StringBuilder>(StringComparer.OrdinalIgnoreCase)
        {
            ["CheckerAgent"] = new(), ["ReflectorAgent"] = new()
        });
    }

    // ── Stream runner ──────────────────────────────────────────────────

    private static async Task RunWorkflowStreamAsync(
        Workflow workflow, List<ChatMessage> input, Dictionary<string, StringBuilder> buffers,
        List<string> eventLog, int cycle, CancellationToken ct)
    {
        var run = await InProcessExecution.RunStreamingAsync(workflow, input);
        await run.TrySendMessageAsync(new TurnToken(emitEvents: true));
        await foreach (var evt in run.WatchStreamAsync().WithCancellation(ct))
        {
            switch (evt)
            {
                case AgentResponseUpdateEvent u: RouteToBuffer(buffers, u.ExecutorId, u.Data?.ToString()); break;
                case AgentResponseEvent r: RouteToBuffer(buffers, r.ExecutorId, r.Data?.ToString(), onlyIfEmpty: true); break;
            }
        }
    }

    private static void RouteToBuffer(Dictionary<string, StringBuilder> bufs, string? id, string? text, bool onlyIfEmpty = false)
    {
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(text)) return;
        var nid = id.Replace('_', '-');
        foreach (var (k, b) in bufs)
            if (nid.StartsWith(k, StringComparison.OrdinalIgnoreCase))
            { if (!onlyIfEmpty || b.Length == 0) b.Append(text); break; }
    }

    // ── Halt helper ────────────────────────────────────────────────────

    private async Task<PmcroResult> HaltResult(
        string trailId, string agent, string seedIntent, int cycle,
        PlannerFrame? pf, string reason,
        MakerFrame? mf = null, CheckerFrame? cf = null)
    {
        pf ??= new PlannerFrame(trailId, seedIntent, "project1", [], "", cycle);
        mf ??= new MakerFrame(trailId, seedIntent, pf, [], false);
        cf ??= new CheckerFrame(trailId, seedIntent, mf, [], false, "");
        var rf = new ReflectorFrame(trailId, seedIntent, LoopDisposition.Halt, "", null, reason, [], cycle, "", null);
        await trailWriter.WriteAsync(agent, trailId, seedIntent, cycle, pf, mf, cf, rf);
        var r = new PmcroResult(Disposition.Halt, cycle, "", reason, seedIntent);
        await trailWriter.SealAsync(agent, trailId, r);
        return r;
    }

    // ── TYPE1 dispatch ──────────────────────────────────────────────────

    private async Task<string> DispatchType1(string raw, string agent, string trailId, CancellationToken ct)
    {
        try
        {
            string? innerJson = null;
            using var doc = JsonDocument.Parse(ExtractJson(raw));
            if (doc.RootElement.TryGetProperty("execution_report", out var rep) && rep.TryGetProperty("tool_calls", out var calls))
                foreach (var c in calls.EnumerateArray())
                    if (c.TryGetProperty("result", out var r) && r.GetString()?.Contains("type1_pending") == true)
                    { innerJson = r.GetString(); break; }
            if (innerJson == null && doc.RootElement.TryGetProperty("artifact", out var af)) innerJson = af.GetString();
            if (innerJson == null || !innerJson.Contains("type1_pending")) return raw;

            using var inner = JsonDocument.Parse(innerJson);
            var pending = inner.RootElement.GetProperty("type1_pending");
            var tool = pending.GetProperty("tool").GetString()!;
            var action = pending.GetProperty("requested_action");

            bool autoApproved = false;
            if (agent == "terminal-agent" && tool == "RunCommand")
            {
                var cmd = action.TryGetProperty("command", out var ce) ? ce.GetString() ?? "" : "";
                var args = action.TryGetProperty("args", out var ae) && ae.ValueKind != JsonValueKind.Null ? ae.GetString() : null;
                var cls = TerminalCommandPolicy.Classify(cmd, args);
                if (cls != TerminalCommandPolicy.Classification.RequiresHil)
                {
                    autoApproved = true;
                    if (cls == TerminalCommandPolicy.Classification.AutoMutating)
                    {
                        var wd = action.TryGetProperty("working_directory", out var we) && we.ValueKind != JsonValueKind.Null ? we.GetString() : null;
                        await mcpToolCache.GitSafetySnapshot(wd, trailId);
                    }
                }
            }

            var approved = autoApproved || await hilChannel.RequestAsync(Guid.NewGuid().ToString("N")[..8], tool, action.ToString(), trailId, ct);
            if (!approved) return WrapDispatch(tool, "{\"success\":false,\"error\":\"HIL_DENIED\"}");

            static string? O(JsonElement e, string p) => e.TryGetProperty(p, out var v) && v.ValueKind != JsonValueKind.Null ? v.GetString() : null;

            string result = agent switch
            {
                "filesystem-agent" when tool == "WriteFile" =>
                    await mcpToolCache.FilesystemExecuteWriteFile(action.GetProperty("path").GetString()!, action.GetProperty("content").GetString() ?? ""),
                "terminal-agent" when tool == "RunCommand" =>
                    await mcpToolCache.TerminalExecuteRunCommand(action.GetProperty("command").GetString()!, O(action, "args"), O(action, "working_directory"), O(action, "slot")),
                _ => "{\"error\":\"unknown agent/tool\"}"
            };
            mcpToolCache.DrainCapturedResults(agent);
            return WrapDispatch(tool!, result);
        }
        catch (Exception ex) { logger.LogError(ex, "[PMCRO-WF] TYPE1 dispatch failed"); return raw; }
    }

    private static string WrapDispatch(string tool, string result) =>
        $"{{\"artifact_type\":\"dispatched\",\"artifact\":\"\",\"execution_report\":{{\"steps_executed\":1,\"tool_calls\":[{{\"step_id\":1,\"tool\":\"{tool}\",\"result\":{result}}}]}}}}";

    // ── Helpers ─────────────────────────────────────────────────────────

    private AIAgent CreateAgent(string name, string instructions, IList<AITool>? tools = null) =>
        chatClient.AsAIAgent(new ChatClientAgentOptions { Name = name, ChatOptions = new ChatOptions { Instructions = instructions, Tools = tools, AdditionalProperties = new() { ["think"] = (object)false } } });

    private string BuildCycleIntent(string seed, string project, string agent, int cycle, string? retry = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"INTENT: {seed}"); sb.AppendLine($"PROJECT: {project}");
        sb.AppendLine($"REAL_PROJECT_ROOT (use this exact path): {config.Value.FileSystemRoot}");
        sb.AppendLine($"SUBJECT_AGENT: {agent}"); sb.AppendLine($"CYCLE: {cycle}");
        if (!string.IsNullOrWhiteSpace(retry)) { sb.AppendLine("RETRY_CONTEXT:"); sb.AppendLine(retry); }
        return sb.ToString().TrimEnd();
    }

    private async Task<string> InjectTerminalPreflight(string intent, CancellationToken ct)
    {
        try
        {
            var words = intent.Replace("INTENT:", "", StringComparison.OrdinalIgnoreCase).Replace("\"", "").Trim().Split(' ');
            var skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "run", "execute", "call" };
            var cmd = words.FirstOrDefault(w => !skip.Contains(w) && !w.Contains(':'));
            if (string.IsNullOrWhiteSpace(cmd)) return intent;
            mcpToolCache.DrainCapturedResults("terminal-agent");
            var which = await mcpToolCache.WhichPreflight(cmd);
            mcpToolCache.DrainCapturedResults("terminal-agent");
            return intent + $"\nPREFLIGHT: '{cmd}' → {which}";
        }
        catch { return intent; }
    }

    private static string ExtractJson(string raw)
    {
        var fs = raw.IndexOf("```json", StringComparison.OrdinalIgnoreCase);
        if (fs >= 0) { var cs = raw.IndexOf('\n', fs) + 1; var fe = raw.IndexOf("```", cs, StringComparison.OrdinalIgnoreCase); if (fe > cs) return raw[cs..fe].Trim(); }
        var os = raw.IndexOf('{'); var as_ = raw.IndexOf('['); var s = (os >= 0 && as_ >= 0) ? Math.Min(os, as_) : Math.Max(os, as_);
        if (s < 0) return raw; var e = Math.Max(raw.LastIndexOf('}'), raw.LastIndexOf(']')); return (e > s) ? raw[s..(e + 1)] : raw[s..];
    }

    // ── Instruction contracts (constant) ───────────────────────────────

    private const string PlannerInstructionsSchema = """
        Output schema: {
          "intent_summary": "...", "assumptions": [], "resource_assumptions": [{ "would_need": "...", "why": "...", "fallback": "human_relay" }],
          "step": { "step_id": 1, "action": "...", "inputs": [], "outputs": [], "agent_or_tool": "..." }, "success_criteria": "..."
        }
        HARD CONSTRAINT: plan EXACTLY ONE atomic action per cycle. "action" MUST be one of the VERIFIED_RESOURCES tool names. Output valid JSON only.
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
}