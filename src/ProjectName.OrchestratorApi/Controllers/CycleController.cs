using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using ProjectName.OrchestratorService;
using ProjectName.OrchestratorService.Configuration;
using ProjectName.OrchestratorService.Loop;
using System.Linq;

namespace ProjectName.OrchestratorApi.Controllers;

[ApiController]
[Route("api")]
[Produces("application/json")]
public class CycleController(
    Orchestrator.OrchestratorClient orchestrator,
    IOptions<OrchestratorConfig> config,
    IHilChannel hilChannel,
    ILogger<CycleController> logger) : ControllerBase
{
    [HttpPost("chat")]
    [ProducesResponseType(typeof(ChatResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Chat([FromBody] ChatRequest req, CancellationToken ct)
    {
        // /pmcro-orchestrator:approve is a control-plane command, not a Seed Intent.
        if (TryParseApproveCommand(req.Message, out var requestId))
        {
            if (!string.IsNullOrWhiteSpace(requestId))
            {
                hilChannel.Resolve(requestId, approved: true);
                return Ok(new ChatResponse(true, req.TrailId ?? string.Empty, "APPROVED",
                    PmcroApprovalOutput("specific request", requestId), 0, null));
            }

            if (!hilChannel.ResolveLatest(approved: true))
                return Conflict(new ChatResponse(false, req.TrailId ?? string.Empty, "NO_PENDING_APPROVAL",
                    PmcroApprovalOutput("latest request", null), 0, "No pending HIL approval exists."));

            return Ok(new ChatResponse(true, req.TrailId ?? string.Empty, "APPROVED",
                PmcroApprovalOutput("latest request", null), 0, null));
        }

        var grpcReq = new CycleRequest
        {
            SeedIntent = req.Message,
            Project = req.Project ?? "pmcro-agent-system",
            SubjectAgent = req.SubjectAgent ?? "filesystem-agent",
            TrailId = req.TrailId ?? Guid.NewGuid().ToString()
        };

        var resp = await orchestrator.RunCycleAsync(grpcReq, cancellationToken: ct);
        return Ok(new ChatResponse(resp.Ok, resp.TrailId, resp.Disposition, resp.FinalOutput,
            resp.CycleNumber, resp.Ok ? null : resp.Error));
    }

    [HttpPost("cycle")]
    [ProducesResponseType(typeof(CycleStarted), StatusCodes.Status202Accepted)]
    public IActionResult StartCycle([FromBody] CycleRequest2 req)
    {
        var trailId = req.TrailId ?? Guid.NewGuid().ToString();
        var grpcReq = new CycleRequest
        {
            SeedIntent = req.Intent,
            Project = req.Project ?? "pmcro-agent-system",
            SubjectAgent = req.SubjectAgent ?? "filesystem-agent",
            TrailId = trailId
        };
        _ = Task.Run(async () => await RunNightShiftChainAsync(grpcReq));
        return Accepted(new CycleStarted(trailId, "running"));
    }

    private async Task RunNightShiftChainAsync(CycleRequest firstRequest)
    {
        var req = firstRequest;
        var maxChained = config.Value.MaxChainedTrails;
        for (int chainLength = 1; chainLength <= maxChained; chainLength++)
        {
            CycleResponse resp;
            try { resp = await orchestrator.RunCycleAsync(req); }
            catch (Exception ex)
            {
                logger.LogError(ex, "[NightShift] Background cycle failed — trail={TrailId} chainLength={N}", req.TrailId, chainLength);
                return;
            }
            var nextSeedIntent = resp.NextSeedIntent;
            if (string.IsNullOrWhiteSpace(nextSeedIntent)) return;
            if (chainLength == maxChained)
            {
                logger.LogWarning("[NightShift] MaxChainedTrails={Max} reached — trail={TrailId}", maxChained, req.TrailId);
                return;
            }
            req = new CycleRequest
            {
                SeedIntent = nextSeedIntent,
                Project = req.Project,
                SubjectAgent = req.SubjectAgent,
                TrailId = Guid.NewGuid().ToString()
            };
        }
    }

    [HttpGet("trail/{trailId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetTrail(string trailId)
    {
        var trailsRoot = Path.Combine(config.Value.FileSystemRoot, ".pmcro", "trails");
        var dispositionPath = Directory.Exists(trailsRoot)
            ? Directory.EnumerateDirectories(trailsRoot)
                .Select(agentDir => Path.Combine(agentDir, trailId, "disposition.json"))
                .FirstOrDefault(System.IO.File.Exists)
            : null;
        if (dispositionPath is null) return Ok(new { trailId, status = "pending" });
        return Content(System.IO.File.ReadAllText(dispositionPath), "application/json");
    }

    [HttpPost("show")]
    public IActionResult Show() => Ok(new { status = "ready" });

    private static bool TryParseApproveCommand(string message, out string? requestId)
    {
        requestId = null;
        if (string.IsNullOrWhiteSpace(message)) return false;
        var trimmed = message.Trim();
        const string command = "/pmcro-orchestrator:approve";
        if (!trimmed.StartsWith(command, StringComparison.OrdinalIgnoreCase)) return false;
        var remainder = trimmed[command.Length..].Trim();
        requestId = string.IsNullOrWhiteSpace(remainder)
            ? null
            : remainder.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
        return true;
    }

    private static string PmcroApprovalOutput(string target, string? requestId) =>
        $"PMCRO\nI AM: The Orchestrator\nI RECEIVE: /pmcro-orchestrator:approve\nI CHECK: Pending governed approval\nI APPROVE: {target}{(requestId is null ? "" : $" ({requestId})")}\nI RECORD: Approval control event\nI RESUME: Governed cycle\nSTATUS: APPROVED";
}

public sealed record ChatRequest(string Message, string? Project = null, string? SubjectAgent = null, string? TrailId = null);
public sealed record ChatResponse(bool Ok, string TrailId, string Disposition, string FinalOutput, int CycleNumber, string? Error);
public sealed record CycleRequest2(string Intent, string? Project = null, string? SubjectAgent = null, string? TrailId = null);
public sealed record CycleStarted(string TrailId, string Status);