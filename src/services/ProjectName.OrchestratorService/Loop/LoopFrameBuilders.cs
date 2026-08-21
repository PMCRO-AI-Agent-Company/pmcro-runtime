// Loop/LoopFrameBuilders.cs
// ARCH-DECLARATIVE-002 (2026-08-06): extracted from PmcroCycleWorkflow's private
// static frame builders so the declarative workflow path (Workflows/Declarative/
// DeclarativeCycleRunner.cs) can parse the SAME raw agent JSON into the SAME
// PlannerFrame/MakerFrame/CheckerFrame/ReflectorFrame shapes FileTrailWriter
// expects, without copy-pasting the parsing logic. Pure move, not a rewrite.
using System.Text;
using System.Text.Json;

namespace ProjectName.OrchestratorService.Loop;

internal static class LoopFrameBuilders
{
    public static PlannerFrame BuildPlannerFrame(string raw, string seedIntent, string project, string trailId, int cycle, string agent)
    {
        var steps = new List<PlanStep>();
        try
        {
            using var doc = JsonDocument.Parse(ExtractJson(raw));
            var root = doc.RootElement;
            if (root.TryGetProperty("step", out var s) && s.ValueKind == JsonValueKind.Object)
            {
                var a = s.TryGetProperty("action", out var ae) ? ae.GetString() ?? "" : "";
                steps.Add(new PlanStep(s.TryGetProperty("step_id", out var si) ? si.GetInt32() : 1, a,
                    s.TryGetProperty("agent_or_tool", out var ao) ? ao.GetString() ?? "" : "", ClassifyType(agent, a), []));
            }
            else if (root.TryGetProperty("steps", out var arr))
            {
                int i = 0;
                foreach (var ss in arr.EnumerateArray())
                { var a = ss.TryGetProperty("action", out var ae) ? ae.GetString() ?? "" : ""; steps.Add(new PlanStep(++i, a, "", ClassifyType(agent, a), [])); }
            }
        }
        catch { }
        string? sc = null;
        try { using var d = JsonDocument.Parse(ExtractJson(raw)); if (d.RootElement.TryGetProperty("success_criteria", out var se) && se.ValueKind == JsonValueKind.String) sc = se.GetString(); } catch { }
        return new PlannerFrame(trailId, seedIntent, project, steps, raw, cycle, sc);
    }

    public static MakerFrame BuildMakerFrame(string raw, PlannerFrame plan, string seedIntent, string trailId)
    {
        var results = new List<StepResult>();
        try
        {
            using var doc = JsonDocument.Parse(ExtractJson(raw));
            var root = doc.RootElement;
            if (root.TryGetProperty("execution_report", out var rep) && rep.TryGetProperty("tool_calls", out var calls))
                foreach (var c in calls.EnumerateArray())
                    results.Add(new StepResult(c.TryGetProperty("step_id", out var si) ? si.GetInt32() : 0,
                        c.TryGetProperty("tool", out var t) ? t.GetString() ?? "" : "",
                        "", c.TryGetProperty("result", out var r) ? r.GetString() ?? "" : "", true));
        }
        catch { }
        return new MakerFrame(trailId, seedIntent, plan, results, results.Count > 0 && results.All(r => r.Ok));
    }

    public static CheckerFrame BuildCheckerFrame(string raw, MakerFrame maker, string seedIntent, string trailId)
    {
        var items = new List<CheckItem>();
        bool allOk = false;
        try
        {
            using var doc = JsonDocument.Parse(ExtractJson(raw));
            var root = doc.RootElement;
            if (root.TryGetProperty("criteria_results", out var cr))
            {
                int i = 0;
                foreach (var c in cr.EnumerateArray())
                {
                    bool passed = c.TryGetProperty("result", out var r) && r.GetString()?.Equals("PASS", StringComparison.OrdinalIgnoreCase) == true;
                    items.Add(new CheckItem(++i, passed, passed ? null : (c.TryGetProperty("rationale", out var ra) ? ra.GetString() : null)));
                }
            }
            allOk = root.TryGetProperty("verdict", out var v) && v.GetString()?.Equals("PASS", StringComparison.OrdinalIgnoreCase) == true;
        }
        catch { }
        return new CheckerFrame(trailId, seedIntent, maker, items, allOk, raw);
    }

    public static ReflectorFrame BuildReflectorFrame(string raw, string finalOutput, string seedIntent, string trailId, int cycle)
    {
        var d = LoopDisposition.Halt; string? rc = null, hr = null, ns = null;
        try
        {
            using var doc = JsonDocument.Parse(ExtractJson(raw));
            var root = doc.RootElement;
            if (root.TryGetProperty("signal", out var s))
                d = s.GetString() switch { "GOAL_COMPLETED" => LoopDisposition.Accept, "RETRY" => LoopDisposition.Retry, _ => LoopDisposition.Halt };
            if (d == LoopDisposition.Retry && root.TryGetProperty("improvements", out var im))
            { var sb = new StringBuilder(); foreach (var imp in im.EnumerateArray()) if (imp.TryGetProperty("suggestion", out var su)) sb.AppendLine(su.GetString()); rc = sb.ToString().Trim(); }
            if (d == LoopDisposition.Halt && root.TryGetProperty("final_output", out var fo)) hr = fo.GetString();
            if (d != LoopDisposition.Retry && root.TryGetProperty("next_seed_intent", out var nsi) && nsi.ValueKind == JsonValueKind.String)
            { var t = nsi.GetString(); if (!string.IsNullOrWhiteSpace(t)) ns = t.Trim(); }
        }
        catch { }
        return new ReflectorFrame(trailId, seedIntent, d, finalOutput, rc, hr, [], cycle, raw, ns);
    }

    public static string ClassifyType(string agent, string action) =>
        action switch
        {
            "WriteFile" when agent == "filesystem-agent" => "TYPE1",
            "RunCommand" or "RunScript" or "KillProcess" when agent == "terminal-agent" => "TYPE1",
            "NavigateTo" or "ClickElement" or "FillInput" or "TakeScreenshot" when agent == "playwright-agent" => "TYPE1",
            _ => "TYPE2"
        };

    public static string ExtractJson(string raw)
    {
        var fs = raw.IndexOf("```json", StringComparison.OrdinalIgnoreCase);
        if (fs >= 0) { var cs = raw.IndexOf('\n', fs) + 1; var fe = raw.IndexOf("```", cs, StringComparison.OrdinalIgnoreCase); if (fe > cs) return raw[cs..fe].Trim(); }
        var os = raw.IndexOf('{'); var as_ = raw.IndexOf('['); var s = (os >= 0 && as_ >= 0) ? Math.Min(os, as_) : Math.Max(os, as_);
        if (s < 0) return raw; var e = Math.Max(raw.LastIndexOf('}'), raw.LastIndexOf(']')); return (e > s) ? raw[s..(e + 1)] : raw[s..];
    }
}
