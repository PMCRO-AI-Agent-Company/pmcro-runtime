// src/ProjectName.OrchestratorApi/Services/TrailReader.cs
// Reads the REAL sealed PMCR-O trails from disk under .pmcro/trails.
// Trails are namespaced: .pmcro/trails/{subjectAgent}/{trailId}/...
// This service never fabricates content — it returns exactly what is on disk.

using System.Text.Json;
using Microsoft.Extensions.Options;
using ProjectName.OrchestratorService.Configuration;

namespace ProjectName.OrchestratorApi.Services;

// FIX (Bug 2 divergence, 2026-07-21): Trail identity resolution now uses the same
// Orchestrator:FileSystemRoot as FileTrailWriter, ensuring writer and reader agree
// on where trails live. Previously TrailReader fell back to env.ContentRootPath,
// which resolved to bin/Debug/net11.0/.pmcro/trails instead of the real trail
// location under the repo root.
public sealed class TrailReader(IOptions<OrchestratorConfig> orchestratorConfig, ILogger<TrailReader> logger)
{
    private string TrailsRoot => Path.Combine(orchestratorConfig.Value.FileSystemRoot, ".pmcro", "trails");

    public IReadOnlyList<string> ListAgents()
    {
        if (!Directory.Exists(TrailsRoot)) return Array.Empty<string>();
        return Directory.EnumerateDirectories(TrailsRoot)
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(n => !n.StartsWith("_") && !n.Equals("README.md", StringComparison.OrdinalIgnoreCase))
            .OrderBy(n => n)
            .ToList();
    }

    public IReadOnlyList<string> ListTrails(string agent)
    {
        var dir = Path.Combine(TrailsRoot, SafeSegment(agent));
        if (!Directory.Exists(dir)) return Array.Empty<string>();
        return Directory.EnumerateDirectories(dir)
            .Select(Path.GetFileName)
            .OfType<string>()
            .OrderBy(n => n)
            .ToList();
    }

    /// <summary>Reads every on-disk artifact of a sealed trail and returns it verbatim.</summary>
    public TrailContent? ReadTrail(string agent, string trailId)
    {
        var dir = Path.Combine(TrailsRoot, SafeSegment(agent), SafeSegment(trailId));
        if (!Directory.Exists(dir))
        {
            logger.LogDebug("[TrailReader] No trail on disk at {Dir}", dir);
            return null;
        }

        string? Read(string name) =>
            File.Exists(Path.Combine(dir, name)) ? File.ReadAllText(Path.Combine(dir, name)) : null;

        var files = Directory.EnumerateFiles(dir)
            .Select(Path.GetFileName)
            .OfType<string>()
            .OrderBy(n => n)
            .ToList();

        return new TrailContent(
            Agent: SafeSegment(agent),
            TrailId: SafeSegment(trailId),
            Files: files,
            Frame: Read("00-frame.json"),
            Plan: Read("01-plan.jsonl"),
            Make: Read("01-make.jsonl"),
            Check: Read("01-check.jsonl"),
            Reflect: Read("01-reflect.jsonl"),
            Disposition: Read("disposition.json"),
            Raw: files.ToDictionary(f => f, f => Read(f)!));
    }

    private static string SafeSegment(string segment) =>
        string.IsNullOrWhiteSpace(segment) ? "_unknown" : segment.Trim().Replace("..", "").Trim('/', '\\');
}

public sealed record TrailContent(
    string Agent,
    string TrailId,
    IReadOnlyList<string> Files,
    string? Frame,
    string? Plan,
    string? Make,
    string? Check,
    string? Reflect,
    string? Disposition,
    IReadOnlyDictionary<string, string> Raw);