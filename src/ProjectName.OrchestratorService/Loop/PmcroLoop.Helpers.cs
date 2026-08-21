// Loop/PmcroLoop.Helpers.cs
// Partial: agents, buffers, frame builders, instructions, JSON helpers.

using System.Text;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace ProjectName.OrchestratorService.Loop;

public sealed partial class PmcroLoop
{
    private AIAgent CreateAgent(string name, string instructions, IList<AITool>? tools = null) =>
        chatClient.AsAIAgent(new ChatClientAgentOptions
        {
            Name = name,
            ChatOptions = new ChatOptions
            {
                Instructions = instructions,
                Tools = tools,
                AdditionalProperties = new() { ["think"] = (object)false }
            }
        });

    private static void RouteToBuffer(Dictionary<string, StringBuilder> buffers, string? executorId, string? text, bool onlyIfEmpty = false)
    {
        if (string.IsNullOrEmpty(executorId) || string.IsNullOrEmpty(text)) return;
        var normalizedExecutorId = executorId.Replace('_', '-');
        foreach (var (key, buf) in buffers)
        {
            if (normalizedExecutorId.StartsWith(key, StringComparison.OrdinalIgnoreCase))
            {
                if (onlyIfEmpty && buf.Length > 0) break;
                buf.Append(text);
                break;
            }
        }
    }

    private static string Truncate(string? s, int max) =>
        string.IsNullOrEmpty(s) ? string.Empty : (s.Length <= max ? s : s[..max] + "\u2026");

    private static string ExtractJson(string raw)
    {
        var fenceStart = raw.IndexOf("```json", StringComparison.OrdinalIgnoreCase);
        if (fenceStart >= 0)
        {
            var contentStart = raw.IndexOf('\n', fenceStart) + 1;
            var fenceEnd = raw.IndexOf("```", contentStart, StringComparison.Ordinal);
            if (fenceEnd > contentStart)
                return raw[contentStart..fenceEnd].Trim();
        }
        var brace = raw.IndexOf('{');
        var end = raw.LastIndexOf('}');
        if (brace >= 0 && end > brace)
            return raw[brace..(end + 1)];
        return raw.Trim();
    }

    private static string BuildAuditInput(PlannerFrame plan, MakerFrame maker, string seedIntent)
    {
        var artifact = string.Join("\n", maker.StepResults.Select(s =>
            "[" + s.Action + "] ok=" + s.Ok + " " + s.Output));
        return "SEED_INTENT: " + seedIntent + "\n"
             + "PLAN: " + plan.RawPlan + "\n"
             + "SUCCESS_CRITERIA: " + plan.SuccessCriteria + "\n"
             + "MAKER_ARTIFACT: " + artifact;
    }

    private PlannerFrame BuildPlannerFrame(string trailId, string seedIntent, string project, int cycle, string raw)
    {
        var steps = new List<PlanStep>();
        string? criteria = null;
        try
        {
            var json = ExtractJson(raw);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("success_criteria", out var sc))
                criteria = sc.GetString();
            if (root.TryGetProperty("steps", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                int i = 0;
                foreach (var el in arr.EnumerateArray())
                {
                    steps.Add(new PlanStep(
                        i++,
                        el.TryGetProperty("action", out var a) ? a.GetString() ?? "unknown" : "unknown",
                        el.TryGetProperty("subject_agent", out var sa) ? sa.GetString() ?? "filesystem-agent" : "filesystem-agent",
                        el.TryGetProperty("action_type", out var at) ? at.GetString() ?? "TYPE2" : "TYPE2",
                        new Dictionary<string, string>()));
                }
            }
        }
        catch { /* keep empty steps */ }

        if (steps.Count == 0)
            steps.Add(new PlanStep(0, "noop", "filesystem-agent", "TYPE2", new()));

        return new PlannerFrame(trailId, seedIntent, project, steps, raw, cycle, criteria);
    }

    private static MakerFrame BuildMakerFrame(string trailId, string seedIntent, PlannerFrame plan, string rawArtifact)
    {
        var results = plan.Steps.Select(s => new StepResult(
            s.Index, s.Action, s.SubjectAgent, rawArtifact, Ok: !string.IsNullOrWhiteSpace(rawArtifact))).ToList();
        return new MakerFrame(trailId, seedIntent, plan, results, results.All(r => r.Ok));
    }

    private static CheckerFrame BuildCheckerFrame(string trailId, string seedIntent, MakerFrame maker, string raw)
    {
        var items = new List<CheckItem>();
        bool allPassed = maker.AllStepsOk;
        try
        {
            var json = ExtractJson(raw);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("all_passed", out var ap))
                allPassed = ap.GetBoolean();
            if (root.TryGetProperty("criteria_results", out var cr) && cr.ValueKind == JsonValueKind.Array)
            {
                int i = 0;
                foreach (var el in cr.EnumerateArray())
                {
                    items.Add(new CheckItem(
                        i++,
                        el.TryGetProperty("passed", out var p) && p.GetBoolean(),
                        el.TryGetProperty("evidence", out var e) ? e.GetString() : null,
                        el.TryGetProperty("criterion", out var c) ? c.GetString() : null));
                }
            }
        }
        catch { /* default */ }

        if (items.Count == 0)
            items.Add(new CheckItem(0, allPassed, null, maker.Plan.SuccessCriteria));

        return new CheckerFrame(trailId, seedIntent, maker, items, allPassed, raw);
    }

    private static ReflectorFrame BuildReflectorFrame(string trailId, string seedIntent, int cycle, CheckerFrame checker, string raw)
    {
        var disposition = LoopDisposition.Halt;
        string? retry = null, halt = null, finalOut = raw, nextSeed = null;
        try
        {
            var json = ExtractJson(raw);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("signal", out var sig))
            {
                disposition = sig.GetString() switch
                {
                    "GOAL_COMPLETED" => LoopDisposition.Accept,
                    "RETRY" => LoopDisposition.Retry,
                    _ => LoopDisposition.Halt
                };
            }
            if (disposition == LoopDisposition.Retry && root.TryGetProperty("improvements", out var impr))
                retry = impr.GetString();
            if (root.TryGetProperty("final_output", out var fo))
                finalOut = fo.GetString() ?? raw;
            if (root.TryGetProperty("halt_reason", out var hr))
                halt = hr.GetString();
            if (root.TryGetProperty("next_seed_intent", out var nsi))
                nextSeed = nsi.GetString();
        }
        catch
        {
            disposition = checker.AllPassed ? LoopDisposition.Accept : LoopDisposition.Retry;
            if (disposition == LoopDisposition.Retry)
                retry = "Checker did not pass; refine plan.";
        }

        return new ReflectorFrame(
            trailId, seedIntent, disposition, finalOut ?? string.Empty,
            retry, halt, new List<EarnedConstraint>(), cycle, raw, nextSeed);
    }

    private string BuildPlannerInstructions(int cycle, string retryContext, string subjectAgentName, List<string> executedActions)
    {
        var executed = executedActions.Count == 0 ? "(none)" : string.Join(", ", executedActions);
        var retryBlock = string.IsNullOrWhiteSpace(retryContext)
            ? string.Empty
            : "RETRY_CONTEXT:\n" + retryContext + "\n";

        // JSON schema shown with ordinary string concat — avoids $""" brace-count CS9006.
        var jsonSchema =
            "{\"steps\":[{\"action\":\"...\",\"subject_agent\":\"" + subjectAgentName +
            "\",\"action_type\":\"TYPE2\"}],\"success_criteria\":\"...\"}";

        return "You are PlannerAgent for PMCR-O cycle " + cycle + ".\n"
             + "Subject agent: " + subjectAgentName + ".\n"
             + "Already executed: " + executed + ".\n"
             + retryBlock
             + "Reply with JSON only:\n"
             + jsonSchema;
    }

    private string BuildCheckerInstructions(string subjectAgentName)
    {
        var law = _skillManifestReader.ReadColonyLaws(subjectAgentName) ?? string.Empty;
        var jsonSchema =
            "{\"all_passed\":true,\"criteria_results\":[{\"criterion\":\"...\",\"passed\":true,\"evidence\":\"...\"}]}";

        return "You are CheckerAgent. Score the maker artifact against success_criteria.\n"
             + "Colony laws for " + subjectAgentName + ":\n"
             + law + "\n"
             + "Reply with JSON only:\n"
             + jsonSchema;
    }

    private static string BuildReflectorInstructions(string seedIntent, List<CumulativeEvidenceEntry> evidence)
    {
        var ev = string.Join("\n", evidence.Select(e =>
            "cycle " + e.Cycle + ": " + e.Action + " passed=" + e.Passed));
        var jsonSchema =
            "{\"signal\":\"GOAL_COMPLETED|RETRY|HALT\",\"final_output\":\"...\",\"improvements\":null,\"next_seed_intent\":null}";

        return "You are ReflectorAgent. Seed intent: " + seedIntent + "\n"
             + "Cumulative evidence:\n"
             + ev + "\n"
             + "Reply with JSON only:\n"
             + jsonSchema;
    }
}
