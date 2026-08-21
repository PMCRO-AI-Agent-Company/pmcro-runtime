using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProjectName.OrchestratorService.Configuration;
using ProjectName.OrchestratorService.Loop;

namespace ProjectName.OrchestratorService.Services;

// FIX (2026-07-11, ARCH-TRAIL-ROOT-001): TrailsRoot was previously hardcoded to
// Path.Combine("S:", ".pmcro", "trails") -- a leftover from an older machine/drive
// mapping, disconnected from the actual repo (this Colony's real trails belong under
// FileSystemRoot's own .pmcro/trails, per GTDDD-MANDATE: no hardcoded paths/limits in
// code, everything sourced from appsettings.json / environment). Now derived from the
// already-configured OrchestratorConfig.FileSystemRoot, consistent with how every other
// path-bearing value in this service (e.g. PmcroLoop's REAL_PROJECT_ROOT) is sourced.
public sealed class FileTrailWriter(ILogger<FileTrailWriter> logger, IOptions<OrchestratorConfig> config) : ITrailWriter
{
    private string TrailsRoot => Path.Combine(config.Value.FileSystemRoot, ".pmcro", "trails");
    private string CostSummaryPath => Path.Combine(TrailsRoot, "cost-summary.json");
    private static readonly JsonSerializerOptions WriteOpts = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public async Task WriteAsync(string subjectAgentName, string trailId, string seedIntent, int cycle, PlannerFrame plan, MakerFrame maker, CheckerFrame checker, ReflectorFrame reflector, long? promptTokens = null, long? completionTokens = null, string? model = null)
    {
        try
        {
            var dir = Path.Combine(TrailsRoot, SafeSegment(subjectAgentName), trailId);
            Directory.CreateDirectory(dir);

            if (cycle == 1)
            {
                var frame = new { trail_id = trailId, seed_intent = seedIntent, started_utc = DateTime.UtcNow.ToString("O") };
                await WriteJsonAsync(Path.Combine(dir, "00-frame.json"), frame);
            }

            var prefix = cycle.ToString("D2");
            await WriteJsonlAsync(Path.Combine(dir, $"{prefix}-plan.jsonl"), plan);
            await WriteJsonlAsync(Path.Combine(dir, $"{prefix}-make.jsonl"), maker);
            await WriteJsonlAsync(Path.Combine(dir, $"{prefix}-check.jsonl"), checker);
            await WriteJsonlAsync(Path.Combine(dir, $"{prefix}-reflect.jsonl"), reflector);

            if (promptTokens.HasValue || completionTokens.HasValue || model is not null)
            {
                var cost = new CycleCost(
                    Cycle: cycle,
                    Model: model ?? "unknown",
                    PromptTokens: promptTokens.GetValueOrDefault(0),
                    CompletionTokens: completionTokens.GetValueOrDefault(0),
                    TotalTokens: (promptTokens.GetValueOrDefault(0) + completionTokens.GetValueOrDefault(0))
                );
                await WriteJsonAsync(Path.Combine(dir, $"{prefix}-cost.json"), cost);
                await AppendCostSummaryAsync(new CostSummaryEntry(trailId, subjectAgentName, seedIntent, cycle, cost));
            }
        }
        catch (Exception ex) { logger.LogError(ex, "[Trail] WriteAsync failed"); }
    }

    public async Task WriteGateAsync(string subjectAgentName, string trailId, int cycle, GateResult gate)
    {
        try
        {
            var dir = Path.Combine(TrailsRoot, SafeSegment(subjectAgentName), trailId);
            Directory.CreateDirectory(dir);
            var prefix = cycle.ToString("D2");
            var gateFileName = gate.GateName.Equals("harness-validation", StringComparison.OrdinalIgnoreCase)
                ? $"{prefix}-harness-validation.jsonl"
                : gate.GateName.Equals("integrity-check", StringComparison.OrdinalIgnoreCase)
                    ? $"{prefix}-integrity-check.jsonl"
                    : gate.GateName.Equals("verdict-audit", StringComparison.OrdinalIgnoreCase)
                        ? $"{prefix}-verdict-audit.jsonl"
                        : gate.GateName.Equals("baton-verification", StringComparison.OrdinalIgnoreCase)
                            ? $"{prefix}-baton-verification.jsonl"
                            : $"{prefix}-{gate.GateName}.jsonl";
            await WriteJsonlAsync(Path.Combine(dir, gateFileName), gate);
        }
        catch (Exception ex) { logger.LogError(ex, "[Trail] WriteGateAsync failed for gate {Gate}", gate.GateName); }
    }

    public async Task SealAsync(string subjectAgentName, string trailId, PmcroResult result)
    {
        try
        {
            var dir = Path.Combine(TrailsRoot, SafeSegment(subjectAgentName), trailId);
            var disposition = new
            {
                TrailId = trailId,
                SeedIntent = result.SeedIntent,
                Disposition = result.Disposition,
                FinalOutput = result.FinalOutput,
                RetryContext = result.RetryContext,
                HaltReason = result.HaltReason,
                EarnedConstraints = Array.Empty<object>(),
                CycleNumber = result.CycleNumber,
                NextSeedIntent = result.NextSeedIntent
            };
            await WriteJsonAsync(Path.Combine(dir, "disposition.json"), disposition);
        }
        catch (Exception ex) { logger.LogError(ex, "[Trail] SealAsync failed"); }
    }

    // --- Round Table session trail (spec: output/pmcro-round-table-live-session-spec.md §3-4) ---
    private string RoundTableRoot => Path.Combine(TrailsRoot, "round-table");
    private string SessionDir(string sessionId) => Path.Combine(RoundTableRoot, SafeSegment(sessionId));
    private string SessionMetaPath(string sessionId) => Path.Combine(SessionDir(sessionId), "session.json");
    private string SessionEntriesPath(string sessionId) => Path.Combine(SessionDir(sessionId), "entries.jsonl");

    public async Task WriteRoundTableSessionAsync(RoundTableSession session)
    {
        try
        {
            Directory.CreateDirectory(SessionDir(session.Id));
            await WriteJsonAsync(SessionMetaPath(session.Id), session);
        }
        catch (Exception ex) { logger.LogError(ex, "[Trail] WriteRoundTableSessionAsync failed for session {SessionId}", session.Id); }
    }

    public async Task<RoundTableSession?> ReadRoundTableSessionAsync(string sessionId)
    {
        try
        {
            var path = SessionMetaPath(sessionId);
            if (!File.Exists(path)) return null;
            var text = await File.ReadAllTextAsync(path);
            return JsonSerializer.Deserialize<RoundTableSession>(text, WriteOpts);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Trail] ReadRoundTableSessionAsync failed for session {SessionId}", sessionId);
            return null;
        }
    }

    public async Task WriteRoundTableEntryAsync(RoundTableEntry entry)
    {
        try
        {
            Directory.CreateDirectory(SessionDir(entry.SessionId));
            var opts = new JsonSerializerOptions(WriteOpts) { WriteIndented = false };
            await File.AppendAllTextAsync(SessionEntriesPath(entry.SessionId), JsonSerializer.Serialize(entry, opts) + Environment.NewLine);
        }
        catch (Exception ex) { logger.LogError(ex, "[Trail] WriteRoundTableEntryAsync failed for session {SessionId}", entry.SessionId); }
    }

    public async Task<IReadOnlyList<RoundTableEntry>> ReadRoundTableEntriesAsync(string sessionId)
    {
        var entries = new List<RoundTableEntry>();
        try
        {
            var path = SessionEntriesPath(sessionId);
            if (!File.Exists(path)) return entries;
            foreach (var line in await File.ReadAllLinesAsync(path))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var entry = JsonSerializer.Deserialize<RoundTableEntry>(line, WriteOpts);
                if (entry is not null) entries.Add(entry);
            }
        }
        catch (Exception ex) { logger.LogError(ex, "[Trail] ReadRoundTableEntriesAsync failed for session {SessionId}", sessionId); }
        return entries;
    }

    private async Task AppendCostSummaryAsync(CostSummaryEntry entry)
    {
        try
        {
            var summaries = new List<CostSummaryEntry>();
            if (File.Exists(CostSummaryPath))
            {
                try
                {
                    var text = await File.ReadAllTextAsync(CostSummaryPath);
                    summaries = JsonSerializer.Deserialize<List<CostSummaryEntry>>(text, WriteOpts) ?? new List<CostSummaryEntry>();
                }
                catch { }
            }

            summaries.Add(entry);
            await File.WriteAllTextAsync(CostSummaryPath, JsonSerializer.Serialize(summaries, WriteOpts));
        }
        catch (Exception ex) { logger.LogError(ex, "[Trail] AppendCostSummaryAsync failed"); }
    }

    private static string SafeSegment(string subjectAgentName) =>
        string.IsNullOrWhiteSpace(subjectAgentName) ? "_unknown-agent" : subjectAgentName;

    private static async Task WriteJsonAsync(string path, object value) => await File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, WriteOpts));
    private static async Task WriteJsonlAsync(string path, object value)
    {
        var opts = new JsonSerializerOptions(WriteOpts) { WriteIndented = false };
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, opts) + Environment.NewLine);
    }

    public sealed record CycleCost(
        int Cycle,
        string Model,
        long PromptTokens,
        long CompletionTokens,
        long TotalTokens
    );

    public sealed record CostSummaryEntry(
        string TrailId,
        string SubjectAgent,
        string SeedIntent,
        int Cycle,
        CycleCost Cost
    );
}