// Workflows/Gates/GateRunner.cs
// Phase gate validators — maps to gate definitions in pmcro.workflow.yaml.
// Uses the existing GateResult record from Loop/LoopFrame.cs.

using Microsoft.Extensions.Logging;
using ProjectName.OrchestratorService.Loop;

namespace ProjectName.OrchestratorService.Workflows.Gates;

public static class GateRunner
{
    public static GateResult PlannerGate(PlannerFrame pf, ILogger logger, int cycle)
    {
        var findings = new List<string>();
        if (pf.Steps.Count == 0) findings.Add("No steps planned");
        if (pf.Steps.Count > 1) findings.Add($"Bounded scope violation: {pf.Steps.Count} steps (max 1)");
        if (string.IsNullOrWhiteSpace(pf.SuccessCriteria)) findings.Add("success_criteria is null or empty");
        var action = pf.Steps.FirstOrDefault()?.Action ?? "";
        if (action.Contains("TODO") || action.Contains("placeholder")) findings.Add($"Placeholder in action: {action}");
        if (findings.Count > 0)
            logger.LogWarning("[PMCRO-WF] Gate 1 (Planner) failed — cycle {Cycle}: {F}", cycle, string.Join("; ", findings));
        return new GateResult("planner-gate", findings.Count == 0, findings);
    }

    public static GateResult MakerGate(MakerFrame mf, ILogger logger, int cycle)
    {
        var findings = new List<string>();
        foreach (var r in mf.StepResults)
            if (r.Output.Contains("TYPE1_PENDING", StringComparison.OrdinalIgnoreCase))
                findings.Add($"Step {r.StepIndex}: unresolved TYPE1_PENDING stub");
        if (findings.Count > 0)
            logger.LogWarning("[PMCRO-WF] Gate 2 (Maker) failed — cycle {Cycle}: {F}", cycle, string.Join("; ", findings));
        return new GateResult("maker-gate", findings.Count == 0, findings);
    }

    public static GateResult CheckerGate(CheckerFrame cf, PlannerFrame pf, ILogger logger, int cycle)
    {
        var findings = new List<string>();
        if (pf?.SuccessCriteria is not null)
        {
            var scItems = pf.SuccessCriteria.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var sc in scItems)
            {
                var scTrim = sc.Trim();
                bool covered = cf.CheckItems.Any(ci =>
                    (ci.FailureEvidence ?? "").Contains(scTrim[..Math.Min(scTrim.Length, 40)], StringComparison.OrdinalIgnoreCase));
                if (!covered)
                    findings.Add($"success_criteria not covered: '{scTrim[..Math.Min(scTrim.Length, 80)]}...'");
            }
        }
        if (findings.Count > 0)
            logger.LogWarning("[PMCRO-WF] Gate 3 (Checker) failed — cycle {Cycle}: {F}", cycle, string.Join("; ", findings));
        return new GateResult("checker-gate", findings.Count == 0, findings);
    }

    public static GateResult ReflectorGate(ReflectorFrame rf, ILogger logger, int cycle)
    {
        var findings = new List<string>();
        if (rf.Disposition == LoopDisposition.Retry && !string.IsNullOrWhiteSpace(rf.NextSeedIntent))
            findings.Add("Retry disposition carries Baton (NextSeedIntent)");
        if (rf.Disposition == LoopDisposition.Halt && string.IsNullOrWhiteSpace(rf.HaltReason))
            findings.Add("Halt disposition missing HaltReason");
        if (rf.NextSeedIntent is not null)
        {
            var n = rf.NextSeedIntent.ToLowerInvariant();
            if (n.Contains("continue") || n.Contains("previous") || n.Contains("before") || n.Contains("as discussed"))
                findings.Add("NextSeedIntent contains deictic phrasing — must be self-contained");
        }
        if (findings.Count > 0)
            logger.LogWarning("[PMCRO-WF] Gate 4 (Reflector) failed — cycle {Cycle}: {F}", cycle, string.Join("; ", findings));
        return new GateResult("reflector-gate", findings.Count == 0, findings);
    }
}