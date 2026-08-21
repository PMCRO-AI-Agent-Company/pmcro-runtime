using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using ProjectName.OrchestratorService;
using ProjectName.OrchestratorService.Configuration;
using System.Linq;

namespace ProjectName.OrchestratorApi.Controllers;

/// <summary>
/// The primary interface for triggering and monitoring PMCR-O cognitive cycles.
/// </summary>
/// <remarks>
/// This controller provides both synchronous (blocking) and asynchronous (fire-and-forget) 
/// methods for executing agent loops. It acts as a REST facade over the backend gRPC Orchestrator.
/// </remarks>
[ApiController]
[Route("api")]
[Produces("application/json")]
public class CycleController(
    Orchestrator.OrchestratorClient orchestrator,
    IOptions<OrchestratorConfig> config,
    ILogger<CycleController> logger) : ControllerBase
{
    /// <summary>
    /// Executes a synchronous PMCR-O cycle (Used by DevUI).
    /// </summary>
    /// <remarks>
    /// Blocks the HTTP request until the loop completes, hits a terminal state (HALT), 
    /// or reaches the 10-minute network timeout.
    /// </remarks>
    /// <param name="req">The request containing the SeedIntent and routing metadata.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A ChatResponse containing the final disposition and artifact output.</returns>
    /// <response code="200">The cycle completed (returns ACCEPT, RETRY, or HALT dispositions).</response>
    [HttpPost("chat")]
    [ProducesResponseType(typeof(ChatResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Chat([FromBody] ChatRequest req, CancellationToken ct)
    {
        var grpcReq = new CycleRequest
        {
            SeedIntent = req.Message,
            Project = req.Project ?? "pmcro-agent-system",
            SubjectAgent = req.SubjectAgent ?? "filesystem-agent",
            TrailId = req.TrailId ?? Guid.NewGuid().ToString()
        };

        var resp = await orchestrator.RunCycleAsync(grpcReq, cancellationToken: ct);

        return Ok(new ChatResponse(
            resp.Ok,
            resp.TrailId,
            resp.Disposition,
            resp.FinalOutput,
            resp.CycleNumber,
            resp.Ok ? null : resp.Error));
    }

    /// <summary>
    /// Starts a PMCRO loop in the background (Fire-and-forget).
    /// </summary>
    /// <remarks>
    /// Returns the TrailId immediately. The cycle runs asynchronously. 
    /// Poll GET /api/trail/{trailId} to check completion status.
    /// </remarks>
    /// <param name="req">The cycle parameters.</param>
    /// <returns>Status indicating the cycle is running.</returns>
    /// <response code="202">Cycle accepted and running in the background.</response>
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

    /// <summary>
    /// NIGHT SHIFT / SUCCESSION LAW: runs one trail, then — if and only if its sealed
    /// disposition carries a non-null NextSeedIntent — automatically starts the next
    /// trail with that as the new SeedIntent, chaining without a human re-triggering
    /// each cycle. TYPE1 actions inside each chained cycle still hit the real HIL gate
    /// exactly as a single manually-triggered cycle would (this method only automates
    /// the trail-to-trail hand-off, never a TYPE1 approval). Bounded by
    /// OrchestratorConfig.MaxChainedTrails since there is no Economic Governor here to
    /// decide when a chain has stopped being worthwhile.
    /// </summary>
    private async Task RunNightShiftChainAsync(CycleRequest firstRequest)
    {
        var req = firstRequest;
        var maxChained = config.Value.MaxChainedTrails;

        for (int chainLength = 1; chainLength <= maxChained; chainLength++)
        {
            CycleResponse resp;
            try
            {
                resp = await orchestrator.RunCycleAsync(req);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[NightShift] Background cycle failed — trail={TrailId} chainLength={N}", req.TrailId, chainLength);
                return;
            }

            var nextSeedIntent = resp.NextSeedIntent;
            if (string.IsNullOrWhiteSpace(nextSeedIntent))
            {
                logger.LogInformation(
                    "[NightShift] Chain stopped — trail={TrailId} disposition={Disp} chainLength={N} (no NextSeedIntent)",
                    req.TrailId, resp.Disposition, chainLength);
                return;
            }

            if (chainLength == maxChained)
            {
                logger.LogWarning(
                    "[NightShift] Chain hit MaxChainedTrails={Max} — stopping even though trail={TrailId} handed off a Baton (\"{Next}\"). Human should review and re-trigger manually if the chain should continue.",
                    maxChained, req.TrailId, Truncate80(nextSeedIntent));
                return;
            }

            var nextTrailId = Guid.NewGuid().ToString();
            logger.LogInformation(
                "[NightShift] Chaining — prior trail={PriorTrailId} → next trail={NextTrailId} chainLength={N}/{Max} intent=\"{Intent}\"",
                req.TrailId, nextTrailId, chainLength + 1, maxChained, Truncate80(nextSeedIntent));

            req = new CycleRequest
            {
                SeedIntent = nextSeedIntent,
                Project = req.Project,
                SubjectAgent = req.SubjectAgent,
                TrailId = nextTrailId
            };
        }
    }

    private static string Truncate80(string s) => s.Length <= 80 ? s : s[..80] + "…";

    /// <summary>
    /// Polls for the disposition of a sealed trail.
    /// </summary>
    /// <remarks>
    /// Reads 'disposition.json' from the configured Orchestrator:FileSystemRoot /
    /// Orchestrator:TrailRoot (fixed ARCH-TRAIL-ROOT-001 recurrence, 2026-07-12 —
    /// this endpoint still had the hardcoded "S:" path that FileTrailWriter's own
    /// fix never touched, so polling could never find trails actually written to
    /// B:\pmcro-cline\.pmcro\trails). Returns a pending status if the cycle is
    /// still running or hasn't sealed yet.
    /// </remarks>
    /// <param name="trailId">The unique ID of the trail to retrieve.</param>
    /// <returns>The final ReflectorFrame or a pending status.</returns>
    /// <response code="200">Returns the trail content or a pending message.</response>
    [HttpGet("trail/{trailId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetTrail(string trailId)
    {
        // ARCH-TRAIL-ROOT-001 (recurrence fix, 2026-07-12): must mirror FileTrailWriter's
        // ACTUAL on-disk layout exactly — {FileSystemRoot}\.pmcro\trails\{subjectAgent}\
        // {trailId}\, NOT OrchestratorConfig.TrailRoot (that value is dead config here;
        // FileTrailWriter never reads it either — see FileTrailWriter.TrailsRoot). This
        // endpoint only receives trailId, not subjectAgent, so it searches the subject-agent
        // subfolders for the one that actually contains this trail rather than guessing.
        var trailsRoot = Path.Combine(config.Value.FileSystemRoot, ".pmcro", "trails");
        var dispositionPath = Directory.Exists(trailsRoot)
            ? Directory.EnumerateDirectories(trailsRoot)
                .Select(agentDir => Path.Combine(agentDir, trailId, "disposition.json"))
                .FirstOrDefault(System.IO.File.Exists)
            : null;

        if (dispositionPath is null)
            return Ok(new { trailId, status = "pending" });

        var json = System.IO.File.ReadAllText(dispositionPath);
        return Content(json, "application/json");
    }

    /// <summary>
    /// Heartbeat endpoint for the DevUI.
    /// </summary>
    /// <remarks>Used by the UI to poll for backend connectivity.</remarks>
    /// <response code="200">The API is active.</response>
    [HttpPost("show")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Show() => Ok(new { status = "ready" });
}

// --- DTOs ---

public sealed record ChatRequest(string Message, string? Project = null, string? SubjectAgent = null, string? TrailId = null);
public sealed record ChatResponse(bool Ok, string TrailId, string Disposition, string FinalOutput, int CycleNumber, string? Error);
public sealed record CycleRequest2(string Intent, string? Project = null, string? SubjectAgent = null, string? TrailId = null);
public sealed record CycleStarted(string TrailId, string Status);