// src/ProjectName.OrchestratorApi/Controllers/RoundTableController.cs
// Phase 1 backend wiring for the Round Table live C-suite session feature.
// See output/pmcro-round-table-live-session-spec.md §3-4.
//
// SCOPE NOTE: this wires the two endpoints the spec's "Next action" line calls
// out (POST /roundtable/sessions, POST /roundtable/sessions/{id}/messages) plus
// a GET for reading a session back, using ITrailWriter directly (same pattern
// TrailReader already uses to talk to disk from this process, not via the gRPC
// client CycleController uses). It does NOT yet implement §4's "Turn generation"
// (attaching each participant to their own PMCR-O macro loop, reading trail
// cycles, generating audience-rendered entries) — that's real LLM-invocation +
// loop-boundary wiring, a separate and larger piece of work than persistence.
// A session created here has participants recorded but no chief loop attached.
//
// CycleRef is required on RoundTableEntry per spec §1 correction (chief entries
// must be trail-grounded). Orchestrator-authored entries (this controller's
// /messages endpoint) aren't grounded on any chief's trail cycle, so CycleRef
// is set to "" for them — flagging this as a judgment call, not a spec-given
// answer, since the spec only defines CycleRef's meaning for chief entries.

using Microsoft.AspNetCore.Mvc;
using ProjectName.OrchestratorService.Loop;
using ProjectName.OrchestratorService.Services;

namespace ProjectName.OrchestratorApi.Controllers;

[ApiController]
[Route("roundtable")]
[Produces("application/json")]
public class RoundTableController(ITrailWriter trailWriter, ILogger<RoundTableController> logger) : ControllerBase
{
    /// <summary>
    /// Starts a new Round Table session. Does not yet attach participants to
    /// their PMCR-O macro loops (see SCOPE NOTE above) — records the session
    /// and its participant list only.
    /// </summary>
    [HttpPost("sessions")]
    [ProducesResponseType(typeof(RoundTableSession), StatusCodes.Status201Created)]
    public async Task<IActionResult> StartSession([FromBody] StartSessionRequest req)
    {
        if (req.Participants is null || req.Participants.Count == 0)
            return BadRequest(new { error = "participants must be a non-empty list of chief agent IDs" });

        var id = Guid.NewGuid().ToString();
        var audience = string.Equals(req.Audience, "business", StringComparison.OrdinalIgnoreCase)
            ? RoundTableAudience.Business
            : RoundTableAudience.Technical;

        var session = new RoundTableSession(
            Id: id,
            Status: RoundTableStatus.Open,
            Audience: audience,
            CreatedAtUtc: DateTime.UtcNow,
            SealedAtUtc: null,
            Participants: req.Participants,
            SessionTrailId: id);

        await trailWriter.WriteRoundTableSessionAsync(session);
        logger.LogInformation("[RoundTable] Session {SessionId} started with {Count} participant(s)", id, req.Participants.Count);

        return CreatedAtAction(nameof(GetSession), new { id }, session);
    }

    /// <summary>
    /// Injects an orchestrator-authored message into a session's timeline.
    /// Writes only — does not call any chief directly (spec §4).
    /// </summary>
    [HttpPost("sessions/{id}/messages")]
    [ProducesResponseType(typeof(RoundTableEntry), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PostMessage(string id, [FromBody] PostMessageRequest req)
    {
        var session = await trailWriter.ReadRoundTableSessionAsync(id);
        if (session is null)
            return NotFound(new { error = $"no session {id}" });

        if (session.Status == RoundTableStatus.Sealed)
            return Conflict(new { error = $"session {id} is sealed" });

        var entry = new RoundTableEntry(
            Id: Guid.NewGuid().ToString(),
            SessionId: id,
            AuthorType: RoundTableAuthorType.Orchestrator,
            AuthorId: "orchestrator",
            Kind: RoundTableEntryKind.Message,
            Content: req.Content,
            CreatedAtUtc: DateTime.UtcNow,
            CycleRef: "");

        await trailWriter.WriteRoundTableEntryAsync(entry);
        return CreatedAtAction(nameof(GetSession), new { id }, entry);
    }

    /// <summary>Reads a session's metadata plus its timeline so far. Not one of the
    /// spec's two named endpoints, but the minimum needed to verify the two above
    /// actually persisted anything.</summary>
    [HttpGet("sessions/{id}")]
    [ProducesResponseType(typeof(RoundTableSessionView), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSession(string id)
    {
        var session = await trailWriter.ReadRoundTableSessionAsync(id);
        if (session is null) return NotFound(new { error = $"no session {id}" });

        var entries = await trailWriter.ReadRoundTableEntriesAsync(id);
        return Ok(new RoundTableSessionView(session, entries));
    }
}

public sealed record StartSessionRequest(IReadOnlyList<string> Participants, string? Audience = null);
public sealed record PostMessageRequest(string Content);
public sealed record RoundTableSessionView(RoundTableSession Session, IReadOnlyList<RoundTableEntry> Entries);
