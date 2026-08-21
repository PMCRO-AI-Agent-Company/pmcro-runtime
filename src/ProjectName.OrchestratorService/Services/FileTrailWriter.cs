// Services/FileTrailWriter.cs
// PMCR-O file-based trail writer. Writes frame JSONs to .pmcro/trails/{trail_id}/.

using System.Text.Json;
using ProjectName.OrchestratorService.Configuration;
using ProjectName.OrchestratorService.Loop;
using Microsoft.Extensions.Options;

namespace ProjectName.OrchestratorService.Services;

public sealed class FileTrailWriter : ITrailWriter
{
    private readonly string _trailRoot;
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public FileTrailWriter(IOptions<OrchestratorConfig> config)
    {
        _trailRoot = Path.GetFullPath(
            Path.Combine(config.Value.FileSystemRoot, config.Value.TrailRoot));
    }

    public async Task WriteAsync(
        string subjectAgent, string trailId, string seedIntent, int cycle,
        PlannerFrame planner, MakerFrame maker, CheckerFrame checker,
        ReflectorFrame reflector, CancellationToken ct = default)
    {
        var dir = Path.Combine(_trailRoot, trailId);
        Directory.CreateDirectory(dir);

        var prefix = $"L{cycle:D2}";
        await WriteJsonAsync(Path.Combine(dir, $"{prefix}-01-planner-frame.json"), planner, ct);
        await WriteJsonAsync(Path.Combine(dir, $"{prefix}-02-maker-frame.json"), maker, ct);
        await WriteJsonAsync(Path.Combine(dir, $"{prefix}-03-checker-frame.json"), checker, ct);
        await WriteJsonAsync(Path.Combine(dir, $"{prefix}-04-reflector-frame.json"), reflector, ct);

        await File.WriteAllTextAsync(
            Path.Combine(dir, "seed-intent.txt"), seedIntent, ct);
        await File.WriteAllTextAsync(
            Path.Combine(dir, "subject-agent.txt"), subjectAgent, ct);
    }

    public async Task SealAsync(
        string subjectAgent, string trailId, PmcroResult result, CancellationToken ct = default)
    {
        var dir = Path.Combine(_trailRoot, trailId);
        Directory.CreateDirectory(dir);
        await WriteJsonAsync(Path.Combine(dir, "seal.json"), result, ct);
    }

    private static async Task WriteJsonAsync<T>(string path, T value, CancellationToken ct)
    {
        await File.WriteAllTextAsync(path,
            JsonSerializer.Serialize(value, JsonOpts), ct);
    }
}
