// Loop/PmcroLoop.cs
// MAF-native PMCRO loop — optimized for Split-Turn Orchestration.
//
// Architecture change (2026-07-03):
//   Splitting into two turns (Maker Turn vs Audit Turn) ensures that if a tool 
//   requires HIL approval (e.g., browser navigation), the Checker genuinely 
//   validates the REAL resulting page content rather than the "Pending" stub.

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProjectName.OrchestratorService.Configuration;
using ProjectName.OrchestratorService.Services;
using ProjectName.OrchestratorService.Skills;

namespace ProjectName.OrchestratorService.Loop;

    public sealed class PmcroLoop(
        IChatClient chatClient,
        McpToolCache mcpToolCache,
        IHilChannel hilChannel,
        ITrailWriter trailWriter,
        IOptions<OrchestratorConfig> config,
        SkillManifestReader skillManifestReader,
        ILogger<PmcroLoop> logger)
    {
        private readonly SkillManifestReader _skillManifestReader = skillManifestReader;
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter() }
    };

    // ── Public entry point ─────────────────────────────────────────────────────

    public async Task<PmcroResult> RunAsync(
        string seedIntent,
        string trailId,
        string project,
        string subjectAgentName,
        AIAgent subjectAgentInstance,
        CancellationToken ct = default)
    {
        var maxCycles = config.Value.MaxLoops;
        var retryContext = string.Empty;

        // ARCH-NEW-003: Deterministic executed-action tracking. RETRY_CONTEXT alone
        // (free-text Reflector advice) wasn't enough to stop the Planner re-choosing
        // the same action every cycle (observed 2026-07-03: NavigateTo planned 3/3
        // cycles despite RETRY_CONTEXT asking for H1/link verification). This list is
        // built from real trail state, not LLM-generated text, and is injected
        // verbatim into the next Planner prompt so "don't repeat NavigateTo" doesn't
        // depend on qwen3:8b correctly inferring it from prose.
        var executedActions = new List<string>();

        // REFLECT-002: Cumulative evidence across cycles. Fixing CHECK-003 (Checker
        // now scores only the current cycle's atomic success_criteria) means a cycle
        // can legitimately PASS on its own narrow terms long before the seed intent
        // as a whole is satisfied. Without this list, the Reflector's old "verdict
        // PASS -> signal GOAL_COMPLETED" mapping would end the trail after cycle 1.
        // This is built from real trail state (not LLM prose) and handed to the
        // Reflector each cycle so it can judge whole-intent completion from the
        // accumulated record instead of this cycle's verdict alone.
        var cumulativeEvidence = new List<CumulativeEvidenceEntry>();

        PlannerFrame? plannerFrame = null;
        MakerFrame? makerFrame = null;
        CheckerFrame? checkerFrame = null;
        ReflectorFrame? reflectorFrame = null;

        for (int cycle = 1; cycle <= maxCycles; cycle++)
        {
            logger.LogInformation(
                "[PMCRO] Cycle {Cycle}/{Max} — trail={TrailId} intent=\"{Intent}\"",
                cycle, maxCycles, trailId, seedIntent);

            // ARCH-AGUI-STATE-001: publish a Planning snapshot at the top of every
            // cycle -- a no-op unless this run is happening inside a bound
            // PmcroStateBridgeAgent AG-UI stream (see Services/PmcroStateBroadcast.cs).
            PmcroStateBroadcast.Publish(new PmcroCycleStateSnapshot(trailId, cycle, "Planning"));

            // On retry cycles the intent message carries the prior Reflector's
            // retry_context so the Planner absorbs corrective feedback.
            var cycleIntent = BuildCycleIntent(seedIntent, project, subjectAgentName, cycle, retryContext);

            // ── EC-PREFLIGHT-001: Terminal preflight ──────────────────────
            if (subjectAgentName == "terminal-agent")
                cycleIntent = await InjectTerminalPreflight(cycleIntent, ct);

            var eventLog = new List<string>();
            string rawPlan, rawArtifact, rawCheck, rawReflection;

            try
            {
                // ── TURN A: Maker workflow (Planner → subject agent) ────────
                // This turn ends after the subject agent produces its artifact.
                var (makerWorkflow, makerBuffers) =
                    BuildMakerWorkflow(cycle, retryContext, subjectAgentInstance, subjectAgentName, executedActions);

                var makerInput = new List<ChatMessage> { new(ChatRole.User, cycleIntent) };

                await RunWorkflowStreamAsync(makerWorkflow, makerInput, makerBuffers, eventLog, cycle, ct);

                rawPlan = makerBuffers["PlannerAgent"].ToString();
                rawArtifact = makerBuffers[subjectAgentInstance.Name ?? subjectAgentName].ToString();

                // DIAG-CODEACT-001 (2026-07-13): temporary diagnostic to confirm
                // whether the subject agent's buffer was genuinely empty (vs. a
                // buffer-key mismatch between subjectAgentInstance.Name and the
                // executor id RouteToBuffer actually matched against) before falling
                // through to SynthesizeArtifact. Logs the raw buffer length, the
                // buffer key used, every buffer key that DID receive content this
                // cycle, and the last few raw streamed events -- ground truth for
                // diagnosing empty-artifact cases (e.g. codeact-agent's persistent
                // StepResults:[] gap) instead of inferring blind. Remove once the
                // codeact-agent capture path is confirmed working end-to-end.
                logger.LogInformation(
                    "[DIAG-CODEACT-001] cycle={Cycle} bufferKey=\"{Key}\" rawArtifactLength={Len} bufferKeysWithContent=[{Keys}] lastEvents=[{Events}]",
                    cycle,
                    subjectAgentInstance.Name ?? subjectAgentName,
                    rawArtifact.Length,
                    string.Join(", ", makerBuffers.Where(kv => kv.Value.Length > 0).Select(kv => $"{kv.Key}:{kv.Value.Length}ch")),
                    string.Join(" | ", eventLog.TakeLast(5)));

                // ── EC-TOOLAGENT-001 / ARCH-DECLARATIVE-004: Synthesis ──────
                // FIX (2026-08-06): previously only synthesized when rawArtifact
                // was EMPTY, silently trusting any non-empty text as already
                // being in the execution_report.tool_calls JSON shape. Real subject
                // agents (e.g. filesystem-agent after a native ReadFile call) often
                // respond in plain natural language instead -- BuildMakerFrame then
                // parsed 0 StepResults even though a real tool call happened. Now
                // synthesizes from real McpToolCache capture data whenever the raw
                // text ISN'T already in the expected schema, not just when empty.
                if (string.IsNullOrWhiteSpace(rawArtifact) || !McpToolCache.HasExecutionReport(rawArtifact))
                {
                    rawArtifact = mcpToolCache.SynthesizeArtifact(subjectAgentName);
                }
                else
                {
                    // Drain and discard capture buffer
                    mcpToolCache.DrainCapturedResults(subjectAgentName);
                }

                // ── ARCH-NEW-001: TYPE1 Dispatch (INTERCEPTION) ───────────
                // If the Maker returned a PENDING stub, run the HIL gate and 
                // execute the real tool NOW so Turn B (Checker) sees real results.
                if (rawArtifact.Contains("TYPE1_PENDING", StringComparison.Ordinal))
                {
                    rawArtifact = await DispatchType1Async(rawArtifact, subjectAgentName, trailId, ct);
                }

                // ── TURN B: Audit workflow (Checker → Reflector) ────────────
                // We reconstruct the conversation thread manually to bridge the split.
                PmcroStateBroadcast.Publish(new PmcroCycleStateSnapshot(trailId, cycle, "Checking"));
                var (auditWorkflow, auditBuffers) = BuildAuditWorkflow(cycle, seedIntent, subjectAgentName, cumulativeEvidence);

                var auditInput = new List<ChatMessage>
                {
                    new(ChatRole.User, cycleIntent),
                    new(ChatRole.Assistant, rawPlan)     { AuthorName = "PlannerAgent" },
                    new(ChatRole.Assistant, rawArtifact) { AuthorName = subjectAgentInstance.Name ?? subjectAgentName }
                };

                await RunWorkflowStreamAsync(auditWorkflow, auditInput, auditBuffers, eventLog, cycle, ct);

                rawCheck = auditBuffers["CheckerAgent"].ToString();
                rawReflection = auditBuffers["ReflectorAgent"].ToString();
                PmcroStateBroadcast.Publish(new PmcroCycleStateSnapshot(trailId, cycle, "Reflecting"));
            }
            catch (OperationCanceledException)
            {
                logger.LogWarning("[PMCRO] Workflow cancelled — cycle {Cycle}", cycle);
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[PMCRO] Workflow exception — cycle {Cycle}", cycle);
                PmcroStateBroadcast.Publish(new PmcroCycleStateSnapshot(trailId, cycle, "Error", Disposition: "Halt"));
                var errResult = new PmcroResult(Disposition.Halt, cycle, string.Empty, ex.Message, seedIntent);
                await trailWriter.SealAsync(subjectAgentName, trailId, errResult);
                return errResult;
            }

            // ── Materialise typed LoopFrame records ────────────────────────
            plannerFrame = BuildPlannerFrame(rawPlan, seedIntent, project, trailId, cycle, subjectAgentName);

            // ── Gate 1: Harness Validation — Planner Frame must be well-formed ──
            var gate1 = RunHarnessValidation(plannerFrame);
            if (!gate1.Passed)
            {
                logger.LogWarning("[PMCRO] Gate 1 failed — cycle {Cycle}: {Findings}", cycle, string.Join("; ", gate1.Findings));
                var emptyMaker = new MakerFrame(trailId, seedIntent, plannerFrame, [], false);
                var emptyChecker = new CheckerFrame(trailId, seedIntent, emptyMaker, [], false, "");
                reflectorFrame = new ReflectorFrame(trailId, seedIntent, LoopDisposition.Halt, string.Empty, null,
                    $"Structural failure — Harness Validation: {string.Join("; ", gate1.Findings)}",
                    [], cycle, rawReflection, null);
                await trailWriter.WriteAsync(subjectAgentName, trailId, seedIntent, cycle,
                    plannerFrame, emptyMaker, emptyChecker, reflectorFrame);
                var err = new PmcroResult(Disposition.Halt, cycle, string.Empty,
                    $"Gate failure: {string.Join("; ", gate1.Findings)}", seedIntent);
                await trailWriter.SealAsync(subjectAgentName, trailId, err);
                return err;
            }

            makerFrame = BuildMakerFrame(rawArtifact, plannerFrame, seedIntent, trailId);

            // ── Gate 2: Integrity Check — Maker Frame must have resolved TYPE1 ──
            var gate2 = RunIntegrityCheck(makerFrame);
            if (!gate2.Passed)
            {
                logger.LogWarning("[PMCRO] Gate 2 failed — cycle {Cycle}: {Findings}", cycle, string.Join("; ", gate2.Findings));
                var emptyChecker = new CheckerFrame(trailId, seedIntent, makerFrame, [], false, "");
                reflectorFrame = new ReflectorFrame(trailId, seedIntent, LoopDisposition.Halt, string.Empty, null,
                    $"Structural failure — Integrity Check: {string.Join("; ", gate2.Findings)}", [],
                    cycle, rawReflection, null);
                await trailWriter.WriteAsync(subjectAgentName, trailId, seedIntent, cycle,
                    plannerFrame, makerFrame, emptyChecker, reflectorFrame);
                var err = new PmcroResult(Disposition.Halt, cycle, string.Empty,
                    $"Gate failure: {string.Join("; ", gate2.Findings)}", seedIntent);
                await trailWriter.SealAsync(subjectAgentName, trailId, err);
                return err;
            }

            checkerFrame = BuildCheckerFrame(rawCheck, makerFrame, seedIntent, trailId);

            // ── Gate 3: Verdict Audit — all success criteria must be covered ───
            var gate3 = RunVerdictAudit(checkerFrame, plannerFrame);
            if (!gate3.Passed)
            {
                logger.LogWarning("[PMCRO] Gate 3 failed — cycle {Cycle}: {Findings}", cycle, string.Join("; ", gate3.Findings));
                reflectorFrame = new ReflectorFrame(trailId, seedIntent, LoopDisposition.Halt, string.Empty, null,
                    $"Structural failure — Verdict Audit: {string.Join("; ", gate3.Findings)}", [],
                    cycle, rawReflection, null);
                await trailWriter.WriteAsync(subjectAgentName, trailId, seedIntent, cycle,
                    plannerFrame, makerFrame, checkerFrame, reflectorFrame);
                var err = new PmcroResult(Disposition.Halt, cycle, string.Empty,
                    $"Gate failure: {string.Join("; ", gate3.Findings)}", seedIntent);
                await trailWriter.SealAsync(subjectAgentName, trailId, err);
                return err;
            }

            reflectorFrame = BuildReflectorFrame(rawReflection, rawReflection, seedIntent, trailId, cycle);

            // ── Gate 4: Baton Verification — only on terminal dispositions ───
            var gate4 = RunBatonVerification(reflectorFrame);
            if (!gate4.Passed)
            {
                logger.LogWarning("[PMCRO] Gate 4 failed — cycle {Cycle}: {Findings}", cycle, string.Join("; ", gate4.Findings));
                reflectorFrame = reflectorFrame with { HaltReason = $"Structural failure — Baton Verification: {string.Join("; ", gate4.Findings)}" };
            }

            // ARCH-NEW-003 (completed): record this cycle's planned action as executed
            // so the next cycle's Planner prompt can deterministically exclude it,
            // instead of relying on the Reflector's free-text RETRY_CONTEXT alone.
            var thisCycleAction = plannerFrame.Steps.FirstOrDefault()?.Action;
            if (!string.IsNullOrWhiteSpace(thisCycleAction) && !executedActions.Contains(thisCycleAction))
                executedActions.Add(thisCycleAction);

            // REFLECT-002: record what this cycle proved (or didn't) for the *next*
            // cycle's Reflector to weigh cumulatively. Deliberately appended AFTER
            // this cycle's own audit ran, so it only ever reflects prior cycles —
            // this cycle's own checker result already reaches the Reflector naturally
            // through the audit workflow's conversation chain.
            cumulativeEvidence.Add(new CumulativeEvidenceEntry(
                Cycle: cycle,
                Action: thisCycleAction ?? "",
                SuccessCriteria: plannerFrame.SuccessCriteria ?? "",
                Passed: checkerFrame.AllPassed));

            await trailWriter.WriteAsync(subjectAgentName, trailId, seedIntent, cycle,
                plannerFrame, makerFrame, checkerFrame, reflectorFrame);

            PmcroStateBroadcast.Publish(new PmcroCycleStateSnapshot(
                trailId, cycle, "CycleComplete",
                LastAction: thisCycleAction,
                Disposition: reflectorFrame.Disposition.ToString(),
                AllPassed: checkerFrame.AllPassed));

            logger.LogInformation(
                "[PMCRO] Cycle {Cycle} complete — disposition={Disp} plan={P}ch artifact={A}ch check={C}ch reflect={R}ch",
                cycle, reflectorFrame.Disposition, rawPlan.Length, rawArtifact.Length, rawCheck.Length, rawReflection.Length);

            // ── ROUTE ─────────────────────────────────────────────────────
            if (reflectorFrame.Disposition != LoopDisposition.Retry)
                break;

            retryContext = reflectorFrame.RetryContext ?? string.Empty;
        }

        // ── Seal trail and return ──────────────────────────────────────────
        var finalDisposition = reflectorFrame!.Disposition switch
        {
            LoopDisposition.Accept => Disposition.Accept,
            _ => Disposition.Halt
        };

        var finalResult = new PmcroResult(
            finalDisposition,
            reflectorFrame.CycleNumber,
            reflectorFrame.FinalOutput,
            finalDisposition == Disposition.Halt ? (reflectorFrame.HaltReason ?? "MaxLoops exceeded") : null,
            seedIntent,
            NextSeedIntent: reflectorFrame.NextSeedIntent);

        await trailWriter.SealAsync(subjectAgentName, trailId, finalResult);
        PmcroStateBroadcast.Publish(new PmcroCycleStateSnapshot(
            trailId, reflectorFrame.CycleNumber, "Sealed",
            Disposition: finalDisposition.ToString()));
        return finalResult;
    }

    // ── Workflow turn executors ──────────────────────────────────────────────

    private async Task RunWorkflowStreamAsync(
        Workflow workflow,
        List<ChatMessage> input,
        Dictionary<string, StringBuilder> buffers,
        List<string> eventLog,
        int cycle,
        CancellationToken ct)
    {
        StreamingRun run = await InProcessExecution.RunStreamingAsync(workflow, input);
        await run.TrySendMessageAsync(new TurnToken(emitEvents: true));

        await foreach (WorkflowEvent evt in run.WatchStreamAsync().WithCancellation(ct))
        {
            switch (evt)
            {
                case AgentResponseUpdateEvent update:
                    RouteToBuffer(buffers, update.ExecutorId, update.Data?.ToString());
                    eventLog.Add($"[DELTA] {update.ExecutorId}: {Truncate(update.Data?.ToString(), 80)}");
                    break;
                case AgentResponseEvent response:
                    RouteToBuffer(buffers, response.ExecutorId, response.Data?.ToString(), onlyIfEmpty: true);
                    eventLog.Add($"[RESPONSE] {response.ExecutorId}: {Truncate(response.Data?.ToString(), 160)}");
                    break;
            }
        }
    }

    private (Workflow workflow, Dictionary<string, StringBuilder> buffers) BuildMakerWorkflow(
        int cycle, string retryContext, AIAgent subjectAgentInstance, string subjectAgentName, List<string> executedActions)
    {
        var plannerAgent = CreateAgent("PlannerAgent", BuildPlannerInstructions(cycle, retryContext, subjectAgentName, executedActions));
        var workflow = new WorkflowBuilder(plannerAgent)
            .WithName($"PmcroMakerTurn_{cycle}")
            .AddEdge(plannerAgent, subjectAgentInstance)
            .Build();

        var buffers = new Dictionary<string, StringBuilder>(StringComparer.OrdinalIgnoreCase)
        {
            ["PlannerAgent"] = new(),
            [subjectAgentInstance.Name ?? subjectAgentName] = new()
        };
        return (workflow, buffers);
    }

    // FIX (2026-07-11, COLONY-LAW-AUDIT-GAP-001): the Checker now receives a
    // lightweight Colony-Law compliance question sourced from the subject agent's
    // own SKILL.md "## Colony Laws" section (same extraction logic ComposeSubjectInstructions
    // in Program.cs uses to prime the Maker). Previously the Checker only ever scored the
    // Planner's self-written success_criteria, so a cycle could Accept honestly while a
    // subject-agent Colony Law was silently never followed. This closure adds ONE
    // additional criteria_results entry ("colony_law_compliance") per cycle so law
    // adherence is at least scanned, not just the atomic success_criteria.
    private (Workflow workflow, Dictionary<string, StringBuilder> buffers) BuildAuditWorkflow(
        int cycle, string seedIntent, string subjectAgentName, List<CumulativeEvidenceEntry> cumulativeEvidence)
    {
        var readTools = mcpToolCache.GetReadTools();
        var checkerAgent = CreateAgent("CheckerAgent", BuildCheckerInstructions(subjectAgentName), readTools);
        var reflectorAgent = CreateAgent("ReflectorAgent", BuildReflectorInstructions(seedIntent, cumulativeEvidence));

        var workflow = new WorkflowBuilder(checkerAgent)
            .WithName($"PmcroAuditTurn_{cycle}")
            .AddEdge(checkerAgent, reflectorAgent)
            .Build();

        var buffers = new Dictionary<string, StringBuilder>(StringComparer.OrdinalIgnoreCase)
        {
            ["CheckerAgent"] = new(),
            ["ReflectorAgent"] = new()
        };
        return (workflow, buffers);
    }

    // ── Logic Helpers ────────────────────────────────────────────────────────

    private static string ExtractJson(string raw)
    {
        var fenceStart = raw.IndexOf("```json", StringComparison.OrdinalIgnoreCase);
        if (fenceStart >= 0)
        {
            var contentStart = raw.IndexOf('\n', fenceStart) + 1;
            var fenceEnd = raw.IndexOf("```", contentStart, StringComparison.OrdinalIgnoreCase);
            if (fenceEnd > contentStart)
                return raw[contentStart..fenceEnd].Trim();
        }

        var objStart = raw.IndexOf('{');
        var arrStart = raw.IndexOf('[');
        var start = (objStart >= 0 && arrStart >= 0) ? Math.Min(objStart, arrStart) : Math.Max(objStart, arrStart);
        if (start < 0) return raw;

        var end = Math.Max(raw.LastIndexOf('}'), raw.LastIndexOf(']'));
        return (end > start) ? raw[start..(end + 1)] : raw[start..];
    }

    private async Task<string> InjectTerminalPreflight(string cycleIntent, CancellationToken ct)
    {
        try
        {
            var intentLine = cycleIntent.Split('\n').FirstOrDefault(l => l.StartsWith("INTENT:", StringComparison.OrdinalIgnoreCase)) ?? "";
            var words = intentLine.Replace("INTENT:", "", StringComparison.OrdinalIgnoreCase).Replace("\"", "").Trim().Split(' ');
            var skipVerbs = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "run", "execute", "call", "terminal-agent:" };
            var command = words.FirstOrDefault(w => !skipVerbs.Contains(w) && !w.Contains(':') && !w.EndsWith("-agent", StringComparison.OrdinalIgnoreCase));

            if (string.IsNullOrWhiteSpace(command)) return cycleIntent;
            mcpToolCache.DrainCapturedResults("terminal-agent");
            var whichResult = await mcpToolCache.WhichPreflight(command);
            mcpToolCache.DrainCapturedResults("terminal-agent");
            return cycleIntent + $"\nPREFLIGHT_CONTEXT: '{command}' resolved at {whichResult}";
        }
        catch { return cycleIntent; }
    }

    private AIAgent CreateAgent(string name, string instructions, IList<AITool>? tools = null) =>
        chatClient.AsAIAgent(new ChatClientAgentOptions
        {
            Name = name,
            ChatOptions = new ChatOptions
            {
                Instructions = instructions,
                Tools = tools,
                AdditionalProperties = new() { ["think"] = (object)false }
            }
        });

    // FIX (2026-07-13, BUG-EXECUTORID-001): MAF's WorkflowBuilder/InProcessExecution
    // generates internal executor IDs that don't always match the raw AIAgent.Name
    // verbatim -- observed for codeact-agent, whose executor ID came through as
    // "codeact_agent_<guid>" (underscore, plus a GUID suffix) while the buffer
    // dictionary key (built from subjectAgentInstance.Name) is "codeact-agent"
    // (hyphen). StartsWith() on the raw strings silently failed for every delta,
    // every cycle -- confirmed via DIAG-CODEACT-001 diagnostic logging, which showed
    // real streamed content ("...directory...", "...updated code...") arriving on
    // an unmatched executor ID while rawArtifact stayed permanently empty. Same bug
    // class as BUG-SKILLNAME-001 (hyphen/underscore mangling), different subsystem.
    // Normalizing underscores to hyphens before the prefix match fixes codeact-agent
    // and guards any future agent whose executor ID gets similarly mangled.
    private static void RouteToBuffer(Dictionary<string, StringBuilder> buffers, string? executorId, string? text, bool onlyIfEmpty = false)
    {
        if (string.IsNullOrEmpty(executorId) || string.IsNullOrEmpty(text)) return;
        var normalizedExecutorId = executorId.Replace('_', '-');
        foreach (var (key, buf) in buffers)
        {
            if (normalizedExecutorId.StartsWith(key, StringComparison.OrdinalIgnoreCase))
            {
                if (onlyIfEmpty && buf.Length > 0) break;
                buf.Append(text);
                break;
            }
        }
    }

    private string BuildCycleIntent(string seedIntent, string project, string subjectAgent, int cycle, string? retryContext = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"INTENT: {seedIntent}");
        sb.AppendLine($"PROJECT: {project}");
        // FIX (2026-07-04): PROJECT above is a caller-supplied label only (e.g. "project1"),
        // never a real filesystem path -- DevUI/callers have no reason to know the real
        // repoRoot. Without this, the Planner had nothing but a bare name to work from when
        // planning terminal-agent shell commands, and hallucinated plausible-looking but
        // fake paths (/path/to/project1, S:\project1\path\to\project1) across every retry
        // cycle instead of converging. REAL_PROJECT_ROOT is the actual resolved
        // Orchestrator__FileSystemRoot value (ground truth, not LLM-inferred) so the
        // Planner can anchor any path-bearing action to something real.
        sb.AppendLine($"REAL_PROJECT_ROOT (ground truth -- use this exact path for any filesystem/terminal action, do not invent or guess a path): {config.Value.FileSystemRoot}");
        sb.AppendLine($"SUBJECT_AGENT: {subjectAgent}");
        sb.AppendLine($"CYCLE: {cycle}");
        if (!string.IsNullOrWhiteSpace(retryContext))
        {
            sb.AppendLine("RETRY_CONTEXT (apply these improvements from the prior cycle):");
            sb.AppendLine(retryContext);
        }
        return sb.ToString().TrimEnd();
    }

    private static string Truncate(string? s, int max) =>
        s is null ? "" : s.Length <= max ? s : s[..max] + "…";

    // ── Frame builders ─────────────────────────────────────────────────────────

    // FIX (2026-07-11, ARCH-ACTIONTYPE-001): ActionType was previously hardcoded to the
    // literal "TYPE2" for every planned step, in both parsing branches below -- meaning
    // every trail's ActionType field was meaningless dead data, for every subject agent,
    // every action, always (confirmed by reading a real sealed trail: a WriteFile step
    // was tagged "TYPE2" even though it's a mutating write). Note this hardcode never
    // actually gated HIL approval -- that's driven entirely by the literal string
    // "TYPE1_PENDING" in a subject agent's raw artifact (see RunAsync's
    // rawArtifact.Contains("TYPE1_PENDING", ...) check) -- but leaving plausible-looking
    // dead data in every trail undermines audit trust, so it's fixed here to reflect a
    // real classification instead of removing the field outright.
    //
    // Classification is a simple per-subject-agent lookup: only the genuinely mutating
    // tool names for each subject agent are TYPE1, everything else is TYPE2. Mirrors the
    // TYPE1/TYPE2 split already documented in each subject agent's tool-set comment in
    // Program.cs (e.g. "Tool set: WriteFile (TYPE1) + ReadFile/ListDirectory/... (TYPE2)").
    private static readonly Dictionary<string, HashSet<string>> Type1ActionsBySubjectAgent =
        new(StringComparer.OrdinalIgnoreCase)
    {
        ["filesystem-agent"] = new(StringComparer.OrdinalIgnoreCase) { "WriteFile" },
        ["terminal-agent"] = new(StringComparer.OrdinalIgnoreCase) { "RunCommand", "RunScript", "KillProcess" },
        ["playwright-agent"] = new(StringComparer.OrdinalIgnoreCase) { "NavigateTo", "ClickElement", "FillInput", "TakeScreenshot" },
    };

    private static string ClassifyActionType(string subjectAgentName, string action)
    {
        if (!string.IsNullOrWhiteSpace(action)
            && Type1ActionsBySubjectAgent.TryGetValue(subjectAgentName, out var type1Actions)
            && type1Actions.Contains(action))
        {
            return "TYPE1";
        }
        return "TYPE2";
    }

    private static PlannerFrame BuildPlannerFrame(string raw, string seedIntent, string project, string trailId, int cycle, string subjectAgentName)
    {
        var steps = new List<PlanStep>();
        try
        {
            using var doc = JsonDocument.Parse(ExtractJson(raw));
            var root = doc.RootElement;

            // ARCH-NEW-002: Planner now emits a single "step" object (bare-minimum
            // planning, PLAN-002/PLAN-003). Prefer that; fall back to the legacy
            // "steps" array if the model didn't comply on a given turn, so a stray
            // format slip doesn't hard-fail the whole cycle.
            if (root.TryGetProperty("step", out var stepEl) && stepEl.ValueKind == JsonValueKind.Object)
            {
                var action1 = stepEl.TryGetProperty("action", out var a1) ? a1.GetString() ?? "" : "";
                steps.Add(new PlanStep(
                    Index: stepEl.TryGetProperty("step_id", out var sid1) ? sid1.GetInt32() : 1,
                    Action: action1,
                    SubjectAgent: stepEl.TryGetProperty("agent_or_tool", out var ao1) ? ao1.GetString() ?? "" : "",
                    ActionType: ClassifyActionType(subjectAgentName, action1),
                    Parameters: []));
            }
            else if (root.TryGetProperty("steps", out var stepsEl))
            {
                int idx = 0;
                foreach (var s in stepsEl.EnumerateArray())
                {
                    var action = s.TryGetProperty("action", out var a) ? a.GetString() ?? "" : "";
                    steps.Add(new PlanStep(
                        Index: s.TryGetProperty("step_id", out var sid) ? sid.GetInt32() : ++idx,
                        Action: action,
                        SubjectAgent: s.TryGetProperty("agent_or_tool", out var ao) ? ao.GetString() ?? "" : "",
                        ActionType: ClassifyActionType(subjectAgentName, action),
                        Parameters: []));
                }
            }
        }
        catch { }

        string? successCriteria = null;
        try
        {
            using var scDoc = JsonDocument.Parse(ExtractJson(raw));
            if (scDoc.RootElement.TryGetProperty("success_criteria", out var scEl) && scEl.ValueKind == JsonValueKind.String)
                successCriteria = scEl.GetString();
        }
        catch { }

        return new PlannerFrame(trailId, seedIntent, project, steps, raw, cycle, successCriteria);
    }

    private static MakerFrame BuildMakerFrame(string raw, PlannerFrame plan, string seedIntent, string trailId)
    {
        var results = new List<StepResult>();
        var allOk = false;
        try
        {
            using var doc = JsonDocument.Parse(ExtractJson(raw));
            var root = doc.RootElement;

            // FIX (StepResults integrity, 2026-07-03): a top-level "error" key alone
            // no longer means "no results happened". DispatchType1Async's dispatched
            // artifacts (and the raw MCP {success,data,error} schema in general)
            // legitimately carry an "error" field on a tool-level failure (e.g. a
            // real ExecuteNavigateTo that hit net::ERR_NAME_NOT_RESOLVED) while still
            // containing a valid execution_report.tool_calls entry that must be
            // preserved as real evidence. Only bail out early when there is no
            // execution_report at all to parse -- that's the genuine "Maker produced
            // nothing usable" case this check was meant to catch.
            var hasExecutionReport = root.TryGetProperty("execution_report", out var rep);

            if (!hasExecutionReport && root.TryGetProperty("error", out _))
                return new MakerFrame(trailId, seedIntent, plan, results, false);

            if (hasExecutionReport && rep.TryGetProperty("tool_calls", out var calls))
            {
                foreach (var c in calls.EnumerateArray())
                {
                    results.Add(new StepResult(
                        StepIndex: c.TryGetProperty("step_id", out var sid) ? sid.GetInt32() : 0,
                        Action: c.TryGetProperty("tool", out var t) ? t.GetString() ?? "" : "",
                        SubjectAgent: "",
                        Output: c.TryGetProperty("result", out var r) ? r.GetString() ?? "" : "",
                        Ok: true));
                }
            }
            allOk = true;
        }
        catch { }
        return new MakerFrame(trailId, seedIntent, plan, results, allOk);
    }

    private static CheckerFrame BuildCheckerFrame(string raw, MakerFrame maker, string seedIntent, string trailId)
    {
        var items = new List<CheckItem>();
        var allPassed = false;
        try
        {
            using var doc = JsonDocument.Parse(ExtractJson(raw));
            var root = doc.RootElement;
            if (root.TryGetProperty("criteria_results", out var cr))
            {
                int idx = 0;
                foreach (var item in cr.EnumerateArray())
                {
                    var passed = item.TryGetProperty("result", out var res) && res.GetString()?.Equals("PASS", StringComparison.OrdinalIgnoreCase) == true;
                    var rationale = item.TryGetProperty("rationale", out var rat) ? rat.GetString() : null;
                    items.Add(new CheckItem(++idx, passed, passed ? null : rationale));
                }
            }
            allPassed = root.TryGetProperty("verdict", out var v) && v.GetString()?.Equals("PASS", StringComparison.OrdinalIgnoreCase) == true;
        }
        catch { }
        return new CheckerFrame(trailId, seedIntent, maker, items, allPassed, raw);
    }

    private static ReflectorFrame BuildReflectorFrame(string raw, string finalOutput, string seedIntent, string trailId, int cycle)
    {
        var disposition = LoopDisposition.Halt;
        string? retryCtx = null;
        string? haltReason = null;
        string? nextSeedIntent = null;
        try
        {
            using var doc = JsonDocument.Parse(ExtractJson(raw));
            var root = doc.RootElement;
            if (root.TryGetProperty("signal", out var sig))
            {
                disposition = sig.GetString() switch { "GOAL_COMPLETED" => LoopDisposition.Accept, "RETRY" => LoopDisposition.Retry, _ => LoopDisposition.Halt };
            }
            if (disposition == LoopDisposition.Retry && root.TryGetProperty("improvements", out var impr))
            {
                var sb = new StringBuilder();
                foreach (var imp in impr.EnumerateArray()) if (imp.TryGetProperty("suggestion", out var sug)) sb.AppendLine(sug.GetString());
                retryCtx = sb.ToString().Trim();
            }
            if (disposition == LoopDisposition.Halt && root.TryGetProperty("final_output", out var fo)) haltReason = fo.GetString();

            // THE SUCCESSION LAW: only ever honoured on a terminal disposition
            // (Accept or Halt) — a Retry cycle isn't done with this trail yet, so
            // any next_seed_intent the model emitted on a Retry cycle is discarded
            // rather than trusted, regardless of what the raw JSON says.
            if (disposition != LoopDisposition.Retry
                && root.TryGetProperty("next_seed_intent", out var nsi)
                && nsi.ValueKind == JsonValueKind.String)
            {
                var text = nsi.GetString();
                nextSeedIntent = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
            }
        }
        catch { }
        return new ReflectorFrame(trailId, seedIntent, disposition, finalOutput, retryCtx, haltReason, new List<EarnedConstraint>(), cycle, raw, nextSeedIntent);
    }

    // ── Instruction Loading (Group 4 Hybrid) ────────────────────────────────

    private static readonly string PlannerInstructions = ComposeInstructions("planner", PlannerInstructionsSchema);
    private static readonly string MakerInstructions = ComposeInstructions("maker", MakerInstructionsSchema);
    private static readonly string CheckerInstructions = ComposeInstructions("checker", CheckerInstructionsSchema);
    private static readonly string ReflectorInstructions = ComposeInstructions("reflector", ReflectorInstructionsSchema);

    private static string ComposeInstructions(string agentFile, string schema)
    {
        var law = LoadAgentLawText(agentFile);
        return string.IsNullOrWhiteSpace(law) ? schema : $"{law}\n\n---\n\n{schema}";
    }

    // ARCH-NATIVE-MAF-001 (2026-07-20): Colony Laws extraction now delegates to
    // SkillManifestReader.ReadColonyLaws, which duplicates the exact same marker
    // logic (start "## Colony Laws", end "## Skill Package Layout") this method
    // used to implement locally against PmcroSkillLoader.GetSkill().FullManifest.
    // PmcroSkillLoader is retired -- SkillManifestReader is the single source for
    // this extraction, shared with Program.cs's ComposeSubjectInstructions.

    // Builds the per-cycle Checker instructions: base checker laws + (if the subject
    // agent has Colony Laws) a lightweight compliance question asking the Checker to add
    // a "colony_law_compliance" criterion to its criteria_results.
    private string BuildCheckerInstructions(string subjectAgentName)
    {
        var baseInstructions = CheckerInstructions;
        var law = _skillManifestReader.ReadColonyLaws(subjectAgentName) ?? "";
        if (string.IsNullOrWhiteSpace(law)) return baseInstructions;

        var compliance = $"""

        ---

        COLONY LAW COMPLIANCE CHECK (closure of COLONY-LAW-AUDIT-GAP-001):
        This cycle's subject agent is '{subjectAgentName}'. Its Colony Laws are:

        {law}

        In ADDITION to the success_criteria check above, add exactly ONE more entry to
        your criteria_results array with criterion = "colony_law_compliance". Judge
        whether this cycle's execution_report and produced artifacts show any VIOLATION of
        the Colony Laws quoted above (e.g. the agent claiming a verification step it cannot
        perform, silently retrying a failed tool call, altering content instead of copying
        it verbatim, or inventing nested/duplicate paths). result = "PASS" if no violation
        is evident from the real evidence; result = "FAIL" with a rationale naming the
        specific law otherwise. This is a lightweight compliance scan, not a full audit —
        base it only on what the raw artifact/tool results actually show.
        """;
        return baseInstructions + compliance;
    }

    private static string LoadAgentLawText(string agentFile)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "skills", "pmcro", "agents", $"{agentFile}.md");
            if (!File.Exists(path)) return "";
            var text = File.ReadAllText(path);
            if (text.StartsWith("---", StringComparison.Ordinal))
            {
                var closeIdx = text.IndexOf("\n---", 3, StringComparison.Ordinal);
                if (closeIdx > 0)
                {
                    var lineEnd = text.IndexOf('\n', closeIdx + 1);
                    text = lineEnd > 0 ? text[(lineEnd + 1)..] : text[(closeIdx + 4)..];
                }
            }
            return text.Trim();
        }
        catch { return ""; }
    }

    private string BuildPlannerInstructions(int cycle, string retryContext, string subjectAgentName, List<string> executedActions)
    {
        var verifiedResources = mcpToolCache.GetVerifiedResourcesJson(subjectAgentName);
        var sb = new StringBuilder(PlannerInstructions);
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine($"VERIFIED_RESOURCES for subject agent '{subjectAgentName}' (PLAN-002 — \"action\" MUST be one of these tool names, exactly, or the Maker has nothing to call):");
        sb.AppendLine(verifiedResources);
        if (executedActions.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"ALREADY_EXECUTED_THIS_TRAIL (deterministic — these actions already ran and produced results already seen by the Checker): {JsonSerializer.Serialize(executedActions)}");
            sb.AppendLine("HARD CONSTRAINT: \"action\" MUST NOT be any value in ALREADY_EXECUTED_THIS_TRAIL, unless VERIFIED_RESOURCES contains no other unused tool name (in which case pick the one most likely to surface the still-missing information). Re-planning an already-executed action wastes a cycle and will not produce new information.");
        }
        if (!string.IsNullOrWhiteSpace(retryContext))
        {
            sb.AppendLine();
            sb.AppendLine($"RETRY CONTEXT FROM PRIOR CYCLE (cycle {cycle - 1}):");
            sb.AppendLine(retryContext);
        }
        return sb.ToString().TrimEnd();
    }

    // REFLECT-002: per-cycle Reflector instructions carrying the seed intent plus
    // the deterministic cumulative-evidence record, so "is the WHOLE trail done"
    // is judged from real accumulated state rather than inferred solely from this
    // cycle's (now atomically-scoped, per CHECK-003) Checker verdict.
    private string BuildReflectorInstructions(string seedIntent, List<CumulativeEvidenceEntry> cumulativeEvidence)
    {
        var sb = new StringBuilder(ReflectorInstructions);
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine($"SEED_INTENT (the whole-trail goal — do not confuse with this cycle's narrow success_criteria): {seedIntent}");
        if (cumulativeEvidence.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("CUMULATIVE_EVIDENCE (deterministic record of every prior cycle in this trail — this cycle's own Checker result is ALSO visible to you above in this conversation and should be added mentally to this list before judging):");
            sb.AppendLine(JsonSerializer.Serialize(cumulativeEvidence.Select(e => new
            {
                cycle = e.Cycle,
                action = e.Action,
                success_criteria = e.SuccessCriteria,
                passed = e.Passed
            })));
            sb.AppendLine("HARD CONSTRAINT (REFLECT-002): a PASS verdict on this cycle's atomic action means ONLY that this cycle's narrow success_criteria was met — it does NOT by itself mean the SEED_INTENT is satisfied. Before emitting signal GOAL_COMPLETED, check the accumulated success_criteria across ALL cycles (this one plus CUMULATIVE_EVIDENCE) and confirm together they cover everything SEED_INTENT asked for. If something SEED_INTENT requires has no corresponding passed cycle yet, emit RETRY instead, so the Planner can plan the next atomic action toward the missing piece.");
        }
        else
        {
            sb.AppendLine();
            sb.AppendLine("CUMULATIVE_EVIDENCE: none yet — this is cycle 1. Before emitting signal GOAL_COMPLETED, confirm this single cycle's action and success_criteria are actually sufficient to fully satisfy SEED_INTENT on their own; if SEED_INTENT clearly asks for more than one atomic action's worth of work, emit RETRY instead.");
        }
        return sb.ToString().TrimEnd();
    }

    // ── JSON Schema Contracts (Constant) ───────────────────────────────────

    private const string PlannerInstructionsSchema = """
        Output schema:
        {
          "intent_summary": "...",
          "assumptions": [],
          "resource_assumptions": [{ "would_need": "...", "why": "...", "fallback": "human_relay" }],
          "step": { "step_id": 1, "action": "...", "inputs": [], "outputs": [], "agent_or_tool": "..." },
          "success_criteria": "..."
        }
        HARD CONSTRAINT (PLAN-002/PLAN-003): plan EXACTLY ONE atomic action per cycle,
        as a single "step" object (not an array). "action" MUST be one of the exact
        tool names listed in VERIFIED_RESOURCES below — never invent an action name
        that isn't in that list. If the goal needs more than one action, plan ONLY
        the single best next action; later actions get planned in later cycles once
        you see this cycle's real result via RETRY_CONTEXT.
        Do NOT execute anything. Output valid JSON only.
        """;

    private const string MakerInstructionsSchema = """
        Output schema:
        {
          "artifact_type": "code | document | data | config | other",
          "artifact": "...",
          "execution_report": {
            "steps_executed": 0,
            "tool_calls": [{ "step_id": 1, "tool": "...", "status": "SUCCESS | FAIL", "result": "..." }],
            "errors": []
          }
        }
        Output valid JSON only.
        """;

    private const string CheckerInstructionsSchema = """
        Output schema:
        {
          "verdict": "PASS | PARTIAL | FAIL",
          "criteria_results": [{ "criterion": "...", "result": "PASS | FAIL", "rationale": "..." }],
          "findings": [{ "severity": "INFO | WARNING | ERROR", "finding": "..." }],
          "artifact_passed_through": true,
          "recommendation": "..."
        }
        Output valid JSON only.
        """;

    private const string ReflectorInstructionsSchema = """
        Output schema:
        {
          "final_output": "...",
          "original_verdict": "PASS | PARTIAL | FAIL",
          "signal": "GOAL_COMPLETED | RETRY | HALT",
          "corrections_applied": [],
          "improvements": [{ "area": "...", "suggestion": "..." }],
          "cycle_summary": "...",
          "ready_for_caller": true,
          "next_seed_intent": "... | null"
        }
        THE SUCCESSION LAW: "next_seed_intent" is how you hand a self-contained Baton
        to the NEXT trail so an autonomous run can continue without a human re-typing
        a new request. Set it to null when signal is RETRY (there is no next trail yet,
        this one isn't done) or when signal is HALT for a reason only a human can resolve
        (missing credentials, ambiguous intent, genuine failure). Set it to a real,
        self-contained next intent when signal is GOAL_COMPLETED and there is obvious
        follow-on work the seed intent implied, or when signal is HALT but only because
        this trail's narrow scope is done and a distinct next step is now unblocked.
        The next_seed_intent text must stand alone — the next trail will NOT have this
        conversation's context, only this string, so name concrete resources/paths, not
        "continue what we were doing." If nothing sensible follows, use null — a stalled
        chain is better than a fabricated one.
        Output valid JSON only.
        """;

    // ── TYPE 1 Dispatcher (ARCH-NEW-001) ────────────────────────────────────

    private async Task<string> DispatchType1Async(string rawArtifact, string agentName, string trailId, CancellationToken ct)
    {
        try
        {
            using var doc = JsonDocument.Parse(ExtractJson(rawArtifact));
            var root = doc.RootElement;

            string? innerJson = null;
            if (root.TryGetProperty("execution_report", out var report) && report.TryGetProperty("tool_calls", out var calls))
            {
                foreach (var call in calls.EnumerateArray())
                {
                    if (call.TryGetProperty("result", out var r) && r.GetString()?.Contains("type1_pending") == true)
                    {
                        innerJson = r.GetString();
                        break;
                    }
                }
            }

            if (innerJson == null && root.TryGetProperty("artifact", out var af))
                innerJson = af.GetString();

            if (innerJson == null || !innerJson.Contains("type1_pending")) return rawArtifact;

            using var innerDoc = JsonDocument.Parse(innerJson);
            var pending = innerDoc.RootElement.GetProperty("type1_pending");
            var tool = pending.GetProperty("tool").GetString();
            var action = pending.GetProperty("requested_action");

            // EC-AUTOAPPROVE-TERM-001: terminal-agent RunCommand gets a tiered
            // classification instead of blanket HIL. AutoReadOnly/AutoMutating skip
            // the human gate (AutoMutating first takes a git safety-snapshot commit --
            // see McpToolCache.GitSafetySnapshot); RequiresHil -- and RunScript/
            // KillProcess, which TerminalCommandPolicy deliberately never classifies --
            // falls through to the existing HIL gate below, unchanged.
            bool autoApproved = false;
            if (agentName == "terminal-agent" && tool == "RunCommand")
            {
                var cmdForPolicy = action.TryGetProperty("command", out var cmdEl) ? cmdEl.GetString() ?? "" : "";
                var argsForPolicy = action.TryGetProperty("args", out var argsEl) && argsEl.ValueKind != JsonValueKind.Null ? argsEl.GetString() : null;
                var classification = TerminalCommandPolicy.Classify(cmdForPolicy, argsForPolicy);

                if (classification != TerminalCommandPolicy.Classification.RequiresHil)
                {
                    autoApproved = true;
                    logger.LogInformation(
                        "[EC-AUTOAPPROVE-TERM-001] Auto-approved (no HIL) -- classification={Class} command={Cmd} {Args} trail={Trail}",
                        classification, cmdForPolicy, argsForPolicy, trailId);

                    if (classification == TerminalCommandPolicy.Classification.AutoMutating)
                    {
                        var workingDirForSnapshot = action.TryGetProperty("working_directory", out var wdEl) && wdEl.ValueKind != JsonValueKind.Null ? wdEl.GetString() : null;
                        await mcpToolCache.GitSafetySnapshot(workingDirForSnapshot, trailId);
                    }
                }
            }

            var approved = autoApproved || await hilChannel.RequestAsync(Guid.NewGuid().ToString("N")[..8], tool!, action.ToString(), trailId, ct);
            if (!approved)
                return WrapDispatchResult(tool ?? "unknown", "{\"success\":false,\"data\":null,\"error\":\"HIL_DENIED\"}");

            string result = "";
            if (agentName == "playwright-agent")
            {
                if (tool == "NavigateTo") result = await mcpToolCache.PlaywrightExecuteNavigateTo(action.GetProperty("url").GetString()!);
                else if (tool == "ClickElement") result = await mcpToolCache.PlaywrightExecuteClickElement(action.GetProperty("selector").GetString()!, null);
                else if (tool == "FillInput") result = await mcpToolCache.PlaywrightExecuteFillInput(action.GetProperty("selector").GetString()!, action.GetProperty("value").GetString()!, null);
                else if (tool == "TakeScreenshot") result = await mcpToolCache.PlaywrightExecuteTakeScreenshot(action.GetProperty("full_page").GetBoolean(), null);
            }
            else if (agentName == "filesystem-agent" && tool == "WriteFile")
            {
                // ARCH-FS-HIL-001: mutating filesystem writes now route through the same
                // HIL gate browser navigation does. This branch only runs because the agent
                // emitted a TYPE1_PENDING stub (maker-facing WriteFile no longer calls the
                // server directly) and the gate above approved it.
                var path = action.GetProperty("path").GetString()!;
                var content = action.GetProperty("content").GetString() ?? "";
                result = await mcpToolCache.FilesystemExecuteWriteFile(path, content);
            }
            else if (agentName == "terminal-agent")
            {
                // ARCH-TERM-HIL-001: terminal-agent's maker-facing RunCommand/RunScript/
                // KillProcess tools now ALWAYS return TYPE1_PENDING (mirroring WriteFile
                // and playwright's TYPE1 tools above) instead of executing unconditionally.
                // This branch is what actually closes the loop — without it, an approved
                // HIL request would have nowhere to route and the action would silently
                // never happen.
                static string? OptStr(JsonElement el, string prop) =>
                    el.TryGetProperty(prop, out var v) && v.ValueKind != JsonValueKind.Null ? v.GetString() : null;

                if (tool == "RunCommand")
                {
                    var command = action.GetProperty("command").GetString()!;
                    result = await mcpToolCache.TerminalExecuteRunCommand(
                        command,
                        OptStr(action, "args"),
                        OptStr(action, "working_directory"),
                        OptStr(action, "slot"));
                }
                else if (tool == "RunScript")
                {
                    var scriptPath = action.GetProperty("script_path").GetString()!;
                    result = await mcpToolCache.TerminalExecuteRunScript(
                        scriptPath,
                        OptStr(action, "args"),
                        OptStr(action, "working_directory"),
                        OptStr(action, "slot"));
                }
                else if (tool == "KillProcess")
                {
                    var processId = action.GetProperty("process_id").GetInt32();
                    result = await mcpToolCache.TerminalExecuteKillProcess(processId, OptStr(action, "slot"));
                }
            }

            // FIX (buffer leakage, 2026-07-03): PlaywrightExecute* routes through
            // CallMcpCapturing, which enqueues into the SAME shared per-agent capture
            // buffer SynthesizeArtifact reads from. Nothing else drained it after a
            // dispatched TYPE1 call, so a stale entry from this cycle's real
            // execution sat in the queue for the *next* cycle to either discard
            // (harmless, observed 2026-07-03 trail 4a6ce6b8) or -- worse -- pick up
            // via SynthesizeArtifact if that next cycle's Maker went silent. Drain
            // it here, immediately after use, every time.
            mcpToolCache.DrainCapturedResults(agentName);

            // FIX (StepResults integrity, 2026-07-03): wrap the real tool result into
            // the same execution_report.tool_calls schema BuildMakerFrame expects
            // everywhere else, instead of returning the raw MCP {success,data,error}
            // payload directly. A tool-level failure (e.g. net::ERR_NAME_NOT_RESOLVED)
            // still means the dispatch itself succeeded and should be preserved as
            // real StepResult data, not discarded via the top-level "error" check.
            return WrapDispatchResult(tool ?? "unknown", result);
        }
        catch (Exception ex) { logger.LogError(ex, "Dispatch Failed"); return rawArtifact; }
    }

    // FIX (2026-07-03): shared wrapper so every DispatchType1Async exit path
    // (HIL-denied or a real executed result) produces the same
    // execution_report.tool_calls schema BuildMakerFrame parses, instead of ad
    // hoc raw JSON that trips its "error" early-return and silently discards
    // real evidence that a tool call actually happened.
    private static string WrapDispatchResult(string tool, string rawResult)
    {
        bool toolSucceeded = true;
        try
        {
            using var d = JsonDocument.Parse(rawResult);
            if (d.RootElement.TryGetProperty("success", out var s) && s.ValueKind == JsonValueKind.False)
                toolSucceeded = false;
        }
        catch { /* not JSON -- treat as opaque success text */ }

        var wrapped = new System.Text.Json.Nodes.JsonObject
        {
            ["artifact_type"] = "type1_dispatched",
            ["artifact"] = rawResult,
            ["execution_report"] = new System.Text.Json.Nodes.JsonObject
            {
                ["steps_executed"] = 1,
                ["tool_calls"] = new System.Text.Json.Nodes.JsonArray(
                    new System.Text.Json.Nodes.JsonObject
                    {
                        ["step_id"] = 1,
                        ["tool"] = tool,
                        ["status"] = toolSucceeded ? "SUCCESS" : "FAIL",
                        ["result"] = rawResult
                    }),
                ["errors"] = toolSucceeded
                    ? new System.Text.Json.Nodes.JsonArray()
                    : new System.Text.Json.Nodes.JsonArray((System.Text.Json.Nodes.JsonNode)rawResult)
            }
        };
        return wrapped.ToJsonString();
    }

    // ── Phase Gate Validators ─────────────────────────────────────────────────────
    // Deterministic, stateless checks against the already-parsed frame data.
    // These are NOT LLM calls — they verify ground truth from the parsed structures.

    // Gate 1: Harness Validation — Law 3 (No Placeholders) + Law 1 (Portability)
    private static GateResult RunHarnessValidation(PlannerFrame plan)
    {
        var findings = new List<string>();
        foreach (var step in plan.Steps)
        {
            // Check for placeholder patterns
            if (!string.IsNullOrWhiteSpace(step.Action) && IsPlaceholderAction(step.Action))
            {
                findings.Add($"Step {step.Index}: Action '{step.Action}' appears to be a placeholder/TODO");
            }
            // Check for hardcoded paths that violate portability
            if (!string.IsNullOrWhiteSpace(step.Action) && ContainsHardcodedPaths(step.Action, plan.RawPlan))
            {
                findings.Add($"Step {step.Index}: Contains hardcoded paths violating Law 1");
            }
        }
        return new GateResult("harness-validation", findings.Count == 0, findings);
    }

    private static bool IsPlaceholderAction(string action) =>
        action.Contains("TODO", StringComparison.OrdinalIgnoreCase) ||
        action.Contains("placeholder", StringComparison.OrdinalIgnoreCase) ||
        action.Contains("stub", StringComparison.OrdinalIgnoreCase) ||
        action.EndsWith("_placeholder", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsHardcodedPaths(string action, string rawPlan) =>
        rawPlan.Contains(@"C:\", StringComparison.Ordinal) ||
        rawPlan.Contains(@"S:\", StringComparison.Ordinal) ||
        rawPlan.Contains("/home/", StringComparison.Ordinal) ||
        rawPlan.Contains("/Users/", StringComparison.Ordinal) ||
        (rawPlan.Contains("path:", StringComparison.OrdinalIgnoreCase) &&
         rawPlan.Contains(@"S:\", StringComparison.Ordinal));

    // Gate 2: Integrity Check — verify StepResults have GroundTruth
    private static GateResult RunIntegrityCheck(MakerFrame maker)
    {
        var findings = new List<string>();
        foreach (var result in maker.StepResults)
        {
            // Check for TYPE1_PENDING stub that wasn't dispatched
            if (result.Output.Contains("TYPE1_PENDING", StringComparison.OrdinalIgnoreCase))
            {
                findings.Add($"Step {result.StepIndex}: Output still contains TYPE1_PENDING stub");
            }
            // Check for missing GroundTruth on actions that should have evidence
            if (result.GroundTruth == null && HasVerifiableAction(result.Action))
            {
                findings.Add($"Step {result.StepIndex}: Missing GroundTruth for verifiable action '{result.Action}'");
            }
            // Check for unverified ground truth claims
            if (result.GroundTruth != null && !result.GroundTruth.Verified)
            {
                findings.Add($"Step {result.StepIndex}: GroundTruth marked as not verified");
            }
        }
        return new GateResult("integrity-check", findings.Count == 0, findings);
    }

    private static readonly HashSet<string> VerifiableActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "WriteFile", "RunCommand", "RunScript", "KillProcess", "NavigateTo", "ClickElement", "FillInput", "TakeScreenshot"
    };

    private static bool HasVerifiableAction(string action) => VerifiableActions.Contains(action);

    // Gate 3: Verdict Audit — check Checker covered all success_criteria with rationale
    private static GateResult RunVerdictAudit(CheckerFrame checker, PlannerFrame plan)
    {
        var findings = new List<string>();
        var successCriteria = plan.SuccessCriteria ?? "";

        if (!string.IsNullOrWhiteSpace(successCriteria))
        {
            // Check if every success criteria has a corresponding CheckItem
            var found = false;
            foreach (var item in checker.CheckItems)
            {
                if (item.FailureEvidence != null && 
                    (successCriteria.Contains(item.FailureEvidence, StringComparison.OrdinalIgnoreCase) ||
                     item.FailureEvidence.Contains(successCriteria.Substring(0, Math.Min(50, successCriteria.Length)), StringComparison.OrdinalIgnoreCase)))
                {
                    found = true;
                    // Check for missing rationale on FAIL
                    if (!item.Passed && string.IsNullOrWhiteSpace(item.FailureEvidence))
                    {
                        findings.Add($"CheckItem {item.StepIndex}: FAIL without rationale");
                    }
                    break;
                }
            }
            if (!found && !checker.AllPassed)
            {
                findings.Add($"success_criteria not covered by any CheckItem: '{successCriteria.Substring(0, Math.Min(50, successCriteria.Length))}...'");
            }
        }

        // Check colony_law_compliance if present
        var colonyCompliance = checker.CheckItems.FirstOrDefault(ci => 
            ci.FailureEvidence != null && ci.FailureEvidence.Contains("colony_law_compliance", StringComparison.OrdinalIgnoreCase));
        if (colonyCompliance != null && !colonyCompliance.Passed && string.IsNullOrWhiteSpace(colonyCompliance.FailureEvidence))
        {
            findings.Add($"colony_law_compliance: FAIL without rationale");
        }

        return new GateResult("verdict-audit", findings.Count == 0, findings);
    }

    // Gate 4: Baton Verification — validate NextSeedIntent and EarnedConstraints
    private static GateResult RunBatonVerification(ReflectorFrame reflector)
    {
        var findings = new List<string>();

        // Check disposition consistency with NextSeedIntent
        if (reflector.Disposition == LoopDisposition.Retry && reflector.NextSeedIntent != null)
        {
            findings.Add($"Retry disposition should not have NextSeedIntent; baton leaked");
        }

        // Check self-containment of NextSeedIntent
        if (reflector.NextSeedIntent != null)
        {
            var baton = reflector.NextSeedIntent;
            if (baton.Contains("continue", StringComparison.OrdinalIgnoreCase) ||
                baton.Contains("previous", StringComparison.OrdinalIgnoreCase) ||
                baton.Contains("before", StringComparison.OrdinalIgnoreCase) ||
                baton.Contains("as discussed", StringComparison.OrdinalIgnoreCase))
            {
                findings.Add($"NextSeedIntent is not self-contained; contains deictic phrasing");
            }
        }

        return new GateResult("baton-verification", findings.Count == 0, findings);
    }
}

// ── Supporting Types ─────────────────────────────────────────────────────────

public record PmcroResult(
    Disposition Disposition,
    int CycleNumber,
    string FinalOutput,
    string? HaltReason = null,
    string? SeedIntent = null,
    string? RetryContext = null,
    // THE SUCCESSION LAW: non-null means the Reflector handed a self-contained
    // Baton to the next trail. A Night Shift runner reads this field — nothing
    // else — to decide whether to chain another cycle. Never synthesized outside
    // BuildReflectorFrame; a missing/null value here always means "stop."
    string? NextSeedIntent = null);

public enum Disposition { Accept, Retry, Halt }
