// Loop/RoundTableFrame.cs
// Types for the Round Table live C-suite multi-agent session feature.
// See output/pmcro-round-table-live-session-spec.md (§3 Data model) for the
// design this implements. A session is a live view across N already-existing
// per-chief trail directories (.pmcro/trails/{chiefId}/**), plus its own thin
// session trail (message log + participant list) under
// .pmcro/trails/round-table/{sessionId}/ so the session itself is durable and
// replayable like everything else in .pmcro/trails/.
//
// CORRECTION (spec §1): a chief's RoundTableEntry is not role-play — it is
// trail-grounded self-simulation. CycleRef is therefore required, not
// optional: it points at the real trail cycle for that chief
// (.pmcro/trails/{authorId}/{cycleRef}/) that the entry's content was
// generated from.
//
// CORRECTION (spec §2): the same trail can render differently depending on
// audience. RoundTableSession.Audience is a rendering-time concern threaded
// into whatever prompt generates each chief's entry text — the underlying
// trail content never changes.

namespace ProjectName.OrchestratorService.Loop;

public enum RoundTableStatus { Open, Sealed }

public enum RoundTableAudience { Technical, Business }

public enum RoundTableAuthorType { Orchestrator, Chief }

public enum RoundTableEntryKind { Message, Plan, Make, Check, Reflect, Disposition }

public sealed record RoundTableSession(
    string                 Id,
    RoundTableStatus       Status,
    RoundTableAudience     Audience,
    DateTime               CreatedAtUtc,
    DateTime?              SealedAtUtc,
    IReadOnlyList<string>  Participants,
    string                 SessionTrailId
);

public sealed record RoundTableEntry(
    string                 Id,
    string                 SessionId,
    RoundTableAuthorType   AuthorType,
    string                 AuthorId,        // "orchestrator" | "cto" | "cfo" | etc.
    RoundTableEntryKind    Kind,
    string                 Content,         // rendered for session.Audience at generation time
    DateTime               CreatedAtUtc,
    string                 CycleRef         // required (spec §1) — .pmcro/trails/{AuthorId}/{CycleRef}/
);
