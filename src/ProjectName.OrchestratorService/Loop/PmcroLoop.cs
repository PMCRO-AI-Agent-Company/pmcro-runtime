// Loop/PmcroLoop.cs
// MAF-native PMCRO loop — optimized for Split-Turn Orchestration.
// Partial class: core RunAsync + workflow builders. Helpers in PmcroLoop.Helpers.cs.

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

public sealed partial class PmcroLoop(
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
        var executedActions = new List<string>();
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

            PmcroStateBroadcast.Publish(new PmcroCycleStateSnapshot(trailId, cycle, "Planning"));

            try
            {
                var (makerWorkflow, makerBuffers) = BuildMakerWorkflow(
                    cycle, retryContext, subjectAgentInstance, subjectAgentName, executedActions);

                var makerInput = new List<ChatMessage>
                {
                    new(ChatRole.User, string.IsNullOrWhiteSpace(retryContext)
                        ? seedIntent
                        : $"{seedIntent}\n\nRETRY_CONTEXT:\n{retryContext}")
                };

                var makerLog = new List<string>();
                await RunWorkflowStreamAsync(makerWorkflow, makerInput, makerBuffers, makerLog, cycle, ct);

                var plannerRaw = makerBuffers.GetValueOrDefault("PlannerAgent")?.ToString() ?? string.Empty;
                var rawArtifact = makerBuffers
                    .FirstOrDefault(kv => !kv.Key.Equals("PlannerAgent", StringComparison.OrdinalIgnoreCase))
                    .Value?.ToString() ?? string.Empty;

                if (string.IsNullOrWhiteSpace(rawArtifact) || !McpToolCache.HasExecutionReport(rawArtifact))
                {
                    logger.LogWarning("[PMCRO] Cycle {Cycle}: maker artifact empty or no execution report", cycle);
                }

                plannerFrame = BuildPlannerFrame(trailId, seedIntent, project, cycle, plannerRaw);
                makerFrame = BuildMakerFrame(trailId, seedIntent, plannerFrame, rawArtifact);

                foreach (var step in plannerFrame.Steps)
                    executedActions.Add($"{step.SubjectAgent}:{step.Action}");

                PmcroStateBroadcast.Publish(new PmcroCycleStateSnapshot(trailId, cycle, "Checking"));

                var (auditWorkflow, auditBuffers) = BuildAuditWorkflow(
                    cycle, seedIntent, subjectAgentName, cumulativeEvidence);

                var auditInput = new List<ChatMessage>
                {
                    new(ChatRole.User, BuildAuditInput(plannerFrame, makerFrame, seedIntent))
                };
                var auditLog = new List<string>();
                await RunWorkflowStreamAsync(auditWorkflow, auditInput, auditBuffers, auditLog, cycle, ct);

                var checkerRaw = auditBuffers.GetValueOrDefault("CheckerAgent")?.ToString() ?? string.Empty;
                var reflectorRaw = auditBuffers.GetValueOrDefault("ReflectorAgent")?.ToString() ?? string.Empty;

                PmcroStateBroadcast.Publish(new PmcroCycleStateSnapshot(trailId, cycle, "Reflecting"));

                checkerFrame = BuildCheckerFrame(trailId, seedIntent, makerFrame, checkerRaw);
                reflectorFrame = BuildReflectorFrame(trailId, seedIntent, cycle, checkerFrame, reflectorRaw);

                cumulativeEvidence.Add(new CumulativeEvidenceEntry(
                    cycle,
                    plannerFrame.Steps.FirstOrDefault()?.Action ?? "unknown",
                    plannerFrame.SuccessCriteria ?? string.Empty,
                    checkerFrame.AllPassed));

                await trailWriter.WriteAsync(
                    subjectAgentName, trailId, seedIntent, cycle,
                    plannerFrame, makerFrame, checkerFrame, reflectorFrame, ct);

                if (reflectorFrame.Disposition == LoopDisposition.Accept)
                    break;
                if (reflectorFrame.Disposition == LoopDisposition.Halt)
                    break;

                retryContext = reflectorFrame.RetryContext ?? string.Empty;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[PMCRO] Cycle {Cycle} failed", cycle);
                PmcroStateBroadcast.Publish(new PmcroCycleStateSnapshot(trailId, cycle, "Error", Disposition: "Halt"));
                var errResult = new PmcroResult(Disposition.Halt, cycle, string.Empty, ex.Message, seedIntent);
                await trailWriter.SealAsync(subjectAgentName, trailId, errResult, ct);
                return errResult;
            }
        }

        reflectorFrame ??= new ReflectorFrame(
            trailId, seedIntent, LoopDisposition.Halt, string.Empty, null,
            "MaxLoops exceeded or no reflector frame", new List<EarnedConstraint>(), maxCycles, string.Empty);

        var finalDisposition = reflectorFrame.Disposition switch
        {
            LoopDisposition.Accept => Disposition.Accept,
            LoopDisposition.Retry => Disposition.Retry,
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
}
