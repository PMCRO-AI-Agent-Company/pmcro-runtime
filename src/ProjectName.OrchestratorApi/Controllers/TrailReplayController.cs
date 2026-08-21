// src/ProjectName.OrchestratorApi/Controllers/TrailReplayController.cs
// Exposes the REAL sealed trails for replay through the chat surface.
// GET /api/trails            -> list agents
// GET /api/trails/{agent}   -> list trail ids for an agent
// GET /api/trails/{agent}/{trailId}
//                            -> returns the verbatim on-disk trail content (C004/C005)
// POST /api/trails/{agent}/{trailId}/replay
//                            -> loads the trail into the Ollama IChatClient as context
//                               and streams a narrated replay back (C005).

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.AI;
using ProjectName.OrchestratorApi.Services;

namespace ProjectName.OrchestratorApi.Controllers;

[ApiController]
[Route("api/trails")]
[Produces("application/json")]
public class TrailReplayController(TrailReader reader, ILogger<TrailReplayController> logger) : ControllerBase
{
    [HttpGet]
    public IActionResult ListAgents() => Ok(new { agents = reader.ListAgents() });

    [HttpGet("{agent}")]
    public IActionResult ListTrails(string agent) => Ok(new { agent, trails = reader.ListTrails(agent) });

    [HttpGet("{agent}/{trailId}")]
    public IActionResult GetTrail(string agent, string trailId)
    {
        var trail = reader.ReadTrail(agent, trailId);
        if (trail is null) return NotFound(new { agent, trailId, error = "trail not found on disk" });
        return Ok(trail);
    }

    [HttpPost("{agent}/{trailId}/replay")]
    public async Task<IActionResult> Replay(string agent, string trailId, [FromServices] IChatClient? ollama, CancellationToken ct)
    {
        var trail = reader.ReadTrail(agent, trailId);
        if (trail is null) return NotFound(new { agent, trailId, error = "trail not found on disk" });

        // Build the replay transcript from the REAL trail files.
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"# PMCR-O Trail Replay — agent={trail.Agent} trailId={trail.TrailId}");
        if (trail.Frame is not null) sb.AppendLine("\n## Frame\n" + trail.Frame);
        if (trail.Plan is not null) sb.AppendLine("\n## PLAN\n" + trail.Plan);
        if (trail.Make is not null) sb.AppendLine("\n## MAKE\n" + trail.Make);
        if (trail.Check is not null) sb.AppendLine("\n## CHECK\n" + trail.Check);
        if (trail.Reflect is not null) sb.AppendLine("\n## REFLECT\n" + trail.Reflect);
        if (trail.Disposition is not null) sb.AppendLine("\n## DISPOSITION\n" + trail.Disposition);

        var transcript = sb.ToString();
        if (string.IsNullOrWhiteSpace(transcript))
            return Ok(new { agent, trailId, replay = transcript, note = "trail files were empty" });

        // If the Ollama IChatClient ("model-orchestrator") is registered and reachable,
        // ask it to narrate the sealed cycle back. Otherwise return the raw transcript.
        if (ollama is not null)
        {
            try
            {
                var messages = new List<ChatMessage>
                {
                    new(ChatRole.System, "You are the PMCRO colony historian. Read the sealed PMCR-O cycle below and narrate it back to a human operator as a clear, faithful replay. Do not invent steps that are not in the trail."),
                    new(ChatRole.User, transcript)
                };
                var response = await ollama.GetResponseAsync(messages, cancellationToken: ct);
                return Ok(new { agent, trailId, model = "model-orchestrator", narration = response.Text, transcript });
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[Replay] Ollama unavailable; returning raw transcript");
                return Ok(new { agent, trailId, model = "model-orchestrator", ollamaError = ex.Message, narration = (string?)null, transcript });
            }
        }

        return Ok(new { agent, trailId, model = (string?)null, narration = (string?)null, transcript });
    }
}