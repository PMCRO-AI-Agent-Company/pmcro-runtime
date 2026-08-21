// src/services/ProjectName.OrchestratorService/Services/ITrailWriter.cs
// Trail writer contract — sealed trail format:
//   .pmcro/trails/{subjectAgentName}/{trailId}/00-frame.json      — seed intent + started_utc (written once on first cycle)
//   .pmcro/trails/{subjectAgentName}/{trailId}/{NN}-plan.jsonl    — PlannerFrame for cycle NN
//   .pmcro/trails/{subjectAgentName}/{trailId}/{NN}-make.jsonl    — MakerFrame for cycle NN
//   .pmcro/trails/{subjectAgentName}/{trailId}/{NN}-check.jsonl   — CheckerFrame for cycle NN
//   .pmcro/trails/{subjectAgentName}/{trailId}/{NN}-reflect.jsonl — ReflectorFrame for cycle NN
//   .pmcro/trails/{subjectAgentName}/{trailId}/disposition.json   — final PmcroResult (written by SealAsync)
// Trails are namespaced by subjectAgentName so a trail's origin is visible from its
// path alone, not just from trailId or the seed_intent inside 00-frame.json.
// A trail is "sealed" only when disposition.json exists with Disposition = Accept|Halt.

using ProjectName.OrchestratorService.Loop;

namespace ProjectName.OrchestratorService.Services;

public interface ITrailWriter
{
    /// <summary>
    /// Write the four phase frames for a single cycle.
    /// Also writes 00-frame.json on the first call (cycle == 1).
    /// </summary>
    Task WriteAsync(
        string subjectAgentName,
        string trailId,
        string seedIntent,
        int    cycle,
        PlannerFrame  plan,
        MakerFrame    maker,
        CheckerFrame  checker,
        ReflectorFrame reflector,
        long? promptTokens = null,
        long? completionTokens = null,
        string? model = null);

    /// <summary>
    /// Write a phase gate result to the trail (NN-{gate-name}.jsonl).
    /// Used for integrity-check, verdict-audit, and baton-verification gates.
    /// </summary>
    Task WriteGateAsync(string subjectAgentName, string trailId, int cycle, GateResult gate);

    /// <summary>
    /// Seal the trail by writing disposition.json.
    /// Must be called exactly once, after the final cycle.
    /// </summary>
    Task SealAsync(string subjectAgentName, string trailId, PmcroResult result);

    // --- Round Table session trail (see output/pmcro-round-table-live-session-spec.md §3-4) ---
    // A session is a live view across N already-existing per-chief trail directories
    // (.pmcro/trails/{chiefId}/**, written via the methods above, unchanged), plus its
    // own thin session trail at .pmcro/trails/round-table/{sessionId}/ so the session
    // itself (message log + participant list) is durable and replayable.

    /// <summary>
    /// Create or update the session's own metadata (session.json). Called on session
    /// start, and again on seal to persist Status=Sealed/SealedAtUtc.
    /// </summary>
    Task WriteRoundTableSessionAsync(RoundTableSession session);

    /// <summary>
    /// Read the session's current metadata, or null if the session does not exist.
    /// </summary>
    Task<RoundTableSession?> ReadRoundTableSessionAsync(string sessionId);

    /// <summary>
    /// Append one entry to the session's timeline (entries.jsonl). Used both for
    /// chief-authored entries generated at a loop boundary and for orchestrator
    /// messages injected via POST /roundtable/sessions/{id}/messages.
    /// </summary>
    Task WriteRoundTableEntryAsync(RoundTableEntry entry);

    /// <summary>
    /// Read all entries appended so far for a session, in append order.
    /// </summary>
    Task<IReadOnlyList<RoundTableEntry>> ReadRoundTableEntriesAsync(string sessionId);
}
