// Loop/LoopFrame.cs
// Typed frame records flowing through the PMCR-O cognitive loop.
// Each phase consumes the previous phase's frame and emits its own.
// All frames are immutable records — mutation produces a new frame.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProjectName.OrchestratorService.Loop;

// EC-RETRY-001: defense-in-depth converter for RetryContext.
// The Reflector service normalises retry_context to a string, but if a model
// or older binary emits an object/array, this converter coerces it to its
// compact JSON representation rather than throwing.
internal sealed class StringOrObjectConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.Null            => null,
            JsonTokenType.String          => reader.GetString(),
            // Any other token (object, array, number, bool) — capture as raw JSON string.
            _                             => CaptureRaw(ref reader),
        };
    }

    private static string CaptureRaw(ref Utf8JsonReader reader)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        return doc.RootElement.GetRawText();
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value is null) writer.WriteNullValue();
        else               writer.WriteStringValue(value);
    }
}

// ── Disposition ───────────────────────────────────────────────────────────────

public enum LoopDisposition { Accept, Retry, Halt }

// ── Cycle Input ───────────────────────────────────────────────────────────────

public sealed record PmcroCycleInput(
    string  SeedIntent,
    string  TrailId,
    string  Project,
    string  SubjectAgent,
    string? RetryContext,
    int     Cycle,
    // Evaluator-Optimizer feedback loop: EarnedConstraints from the prior
    // ReflectorFrame are passed back into each RETRY cycle's PlanRequest.
    // Planner injects them as ACTIVE_CONSTRAINTS — Gate 2 MUST check them.
    IReadOnlyList<EarnedConstraint>? ActiveConstraints = null
);

// ── Step (Planner output unit) ────────────────────────────────────────────────

public sealed record PlanStep(
    int    Index,
    string Action,         // domain action key e.g. "fs_write_file"
    string SubjectAgent,   // "filesystem-agent" | "web-agent" | "code-agent"
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
    string RawPlan,        // LLM output for audit
    int    CycleNumber = 1,
    string? SuccessCriteria = null   // this cycle's atomic success_criteria (CHECK-003 / REFLECT-002 scope)
);

// ── Step Result (Maker output unit) ──────────────────────────────────────────

public sealed record StepResult(
    int    StepIndex,
    string Action,
    string SubjectAgent,
    string Output,         // JSON string from tool call
    bool   Ok,
    string? Error = null,
    GroundTruth? GroundTruth = null
);

public sealed record GroundTruth(
    string Method,         // "mcp_tool_call" | "exit_code" | "browser_snapshot"
    bool   Verified,
    string Evidence        // JSON or text snippet from environment
);

// ── Maker Frame ───────────────────────────────────────────────────────────────

public sealed record MakerFrame(
    string TrailId,
    string SeedIntent,
    PlannerFrame Plan,
    List<StepResult> StepResults,
    bool AllStepsOk
);

// ── Checker Frame ─────────────────────────────────────────────────────────────

public sealed record CheckItem(
    int    StepIndex,
    bool   Passed,
    string? FailureEvidence = null,
    string? RetryHint = null,
    // FIX (GATE3-CRITERION-001, 2026-07-22): the Checker's raw JSON always names
    // each criteria_results entry (e.g. "One Bounded Action Per Cycle",
    // "colony_law_compliance"), but BuildCheckerFrame previously discarded that
    // name entirely. Gate 3 (RunVerdictAudit) and the colony_law_compliance
    // lookup were both trying to re-derive which item was which by fuzzy
    // substring-matching FailureEvidence text instead — a check that's only
    // ever non-null on a FAIL, so it could never match a PASSED item, and never
    // matched cleanly on a FAIL either since rationale prose rarely echoes the
    // success_criteria text. Storing the real criterion name lets both callers
    // match by identity instead of guessing from prose.
    string? Criterion = null
);

public sealed record CheckerFrame(
    string TrailId,
    string SeedIntent,
    MakerFrame MakerOutput,
    List<CheckItem> CheckItems,
    bool AllPassed,
    string RawVerdict      // LLM output for audit
);

// ── Reflector Frame ───────────────────────────────────────────────────────────

public sealed record EarnedConstraint(
    string Id,
    string Rule,
    string TriggeredBy
);

public sealed record ReflectorFrame(
    string            TrailId,
    string            SeedIntent,
    LoopDisposition   Disposition,   // Accept | Retry | Halt
    string            FinalOutput,   // Artifact text or summary
    [property: JsonConverter(typeof(StringOrObjectConverter))]
    string?           RetryContext,  // Non-null on Retry — fed back to Planner
    string?           HaltReason,   // Non-null on Halt
    List<EarnedConstraint> EarnedConstraints,
    int               CycleNumber,
    string            RawReflection,  // LLM output for audit
    // THE SUCCESSION LAW (registry.md §6 / reflector.md §4.1) — documented since
    // T1-succession-law-upgrade-001 but never actually wired into the loop until now.
    // Non-null on a terminal Accept (or a mission-partial Halt with follow-on work)
    // means the Reflector is handing a self-contained Baton to the *next* trail.
    // Null means the chain stops here — either true mission completion, or a Halt
    // that needs a human, not another autonomous cycle.
    string?           NextSeedIntent = null
);

// ── Phase Gate Result ──────────────────────────────────────────────────────────
// Deterministic, stateless validation result from each phase gate. Not an LLM call.
public sealed record GateResult(
    string GateName,
    bool   Passed,
    IReadOnlyList<string> Findings
);

// ── Cumulative Evidence (REFLECT-002) ─────────────────────────────────────────
// One entry per completed cycle, threaded across the whole trail so the
// Reflector can judge whole-intent completion from accumulated atomic results
// instead of conflating "this cycle's narrow verdict was PASS" with
// "the seed intent is fully satisfied" (see checker.md CHECK-004).
public sealed record CumulativeEvidenceEntry(
    int    Cycle,
    string Action,
    string SuccessCriteria,
    bool   Passed
);
