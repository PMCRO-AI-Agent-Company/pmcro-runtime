// Loop/LoopFrame.cs
// PMCR-O typed frame records — immutable, flowing through Plan→Make→Check→Reflect.
// Each phase consumes the previous frame and emits its own.
// Ported from canonical PMCR-O-Marketplace implementation (2026-08-21).

namespace ProjectName.OrchestratorService.Loop;

// ── Disposition ───────────────────────────────────────────────────────────────

public enum LoopDisposition { Accept, Retry, Halt }

// ── PlanStep (Planner output unit) ────────────────────────────────────────────

public sealed record PlanStep(
    int Index,
    string Action,         // domain action key e.g. "ListDirectory"
    string SubjectAgent,   // "filesystem-agent" | "terminal-agent" | "playwright-agent"
    string ActionType,     // "TYPE1" | "TYPE2"
    Dictionary<string, string> Parameters,
    string? HilToken = null
);

// ── Planner Frame ─────────────────────────────────────────────────────────────

public sealed record PlannerFrame(
    string TrailId,
    string SeedIntent,
    string Project,
    List<PlanStep> Steps,
    string RawPlan,        // LLM output preserved for audit
    int CycleNumber = 1,
    string? SuccessCriteria = null
);

// ── StepResult (Maker output unit) ────────────────────────────────────────────

public sealed record StepResult(
    int StepIndex,
    string Action,
    string SubjectAgent,
    string Output,         // JSON string from tool call
    bool Ok,
    string? Error = null
);

// ── Maker Frame ───────────────────────────────────────────────────────────────

public sealed record MakerFrame(
    string TrailId,
    string SeedIntent,
    PlannerFrame Plan,
    List<StepResult> StepResults,
    bool AllStepsOk
);

// ── CheckItem ─────────────────────────────────────────────────────────────────

public sealed record CheckItem(
    int StepIndex,
    bool Passed,
    string? FailureEvidence = null,
    string? Criterion = null
);

// ── Checker Frame ─────────────────────────────────────────────────────────────

public sealed record CheckerFrame(
    string TrailId,
    string SeedIntent,
    MakerFrame MakerOutput,
    List<CheckItem> CheckItems,
    bool AllPassed,
    string RawVerdict       // LLM output preserved for audit
);

// ── EarnedConstraint ──────────────────────────────────────────────────────────

public sealed record EarnedConstraint(
    string Id,
    string Rule,
    string TriggeredBy
);

// ── Reflector Frame ───────────────────────────────────────────────────────────

public sealed record ReflectorFrame(
    string TrailId,
    string SeedIntent,
    LoopDisposition Disposition,    // Accept | Retry | Halt
    string FinalOutput,             // Artifact text or summary
    string? RetryContext,           // Non-null on Retry — fed back to Planner next cycle
    string? HaltReason,             // Non-null on Halt
    List<EarnedConstraint> EarnedConstraints,
    int CycleNumber,
    string RawReflection,           // LLM output preserved for audit
    string? NextSeedIntent = null   // THE SUCCESSION LAW: baton to next trail (terminal Accept/Halt only)
);

// ── Gate Result ───────────────────────────────────────────────────────────────

public sealed record GateResult(
    string GateName,
    bool Passed,
    IReadOnlyList<string> Findings
);

// ── Cumulative Evidence (REFLECT-002) ─────────────────────────────────────────
// One entry per completed cycle, threaded across the whole trail so the
// Reflector can judge whole-intent completion from accumulated atomic results.

public sealed record CumulativeEvidenceEntry(
    int Cycle,
    string Action,
    string SuccessCriteria,
    bool Passed
);

// ── Cycle Result ──────────────────────────────────────────────────────────────

public enum Disposition { Accept, Retry, Halt }

public sealed record PmcroResult(
    Disposition Disposition,
    int CycleNumber,
    string? FinalOutput,
    string? HaltReason = null,
    string? SeedIntent = null,
    string? RetryContext = null,
    string? NextSeedIntent = null
);

// ── Subject Agent Registry ────────────────────────────────────────────────────

public interface ISubjectAgentRegistry
{
    Microsoft.Agents.AI.AIAgent? Resolve(string name);
    void Register(string name, Microsoft.Agents.AI.AIAgent agent);
}
