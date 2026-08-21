using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProjectName.OrchestratorService.Configuration;

namespace ProjectName.OrchestratorService.Skills;

/// <summary>
/// Reads the canonical marketplace catalog used by the frontend. Native MAF
/// execution still reads the materialized staging root through AgentSkillsProvider.
/// </summary>
public sealed class SkillCatalogService(
    ILogger<SkillCatalogService> logger,
    IOptions<OrchestratorConfig> config)
{
    private static readonly Regex NamePattern = new(
        "^name:\\s*[\\\"']?([^\\\"'\\r\\n]+)",
        RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex DescriptionPattern = new(
        "^description:\\s*[\\\"']?([^\\\"'\\r\\n]+)",
        RegexOptions.Multiline | RegexOptions.Compiled);

    public SkillCatalogSnapshot GetSnapshot(string? query = null)
    {
        var entries = Search(query);
        return new SkillCatalogSnapshot(
            Source: config.Value.MarketplaceRelativePath,
            Count: entries.Count,
            Plugins: entries.Select(entry => entry.Plugin).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            Skills: entries);
    }

    public IReadOnlyList<SkillCatalogEntry> Search(string? query = null)
    {
        var entries = new Dictionary<string, SkillCatalogEntry>(StringComparer.OrdinalIgnoreCase);
        var marketplacePath = Path.Combine(
            config.Value.FileSystemRoot,
            config.Value.MarketplaceRelativePath.Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(marketplacePath)) return [];

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(marketplacePath));
            if (!document.RootElement.TryGetProperty("plugins", out var plugins)) return [];

            foreach (var plugin in plugins.EnumerateArray())
            {
                var pluginName = plugin.GetProperty("name").GetString();
                var source = plugin.GetProperty("source").GetString();
                if (string.IsNullOrWhiteSpace(pluginName) || string.IsNullOrWhiteSpace(source)) continue;

                var skillsRoot = Path.Combine(Path.GetFullPath(Path.Combine(config.Value.FileSystemRoot, source)), "skills");
                if (!Directory.Exists(skillsRoot)) continue;

                foreach (var skillDirectory in Directory.EnumerateDirectories(skillsRoot))
                {
                    var manifestPath = Path.Combine(skillDirectory, "SKILL.md");
                    if (!File.Exists(manifestPath)) continue;
                    AddManifest(entries, pluginName, manifestPath, query);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to read canonical marketplace skill catalog from {Path}", marketplacePath);
        }

        return entries.Values
            .OrderBy(entry => entry.Plugin, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
    private string? ResolveMarketplacePath()
    {
        var relativePath = config.Value.MarketplaceRelativePath.Replace('/', Path.DirectorySeparatorChar);
        var candidates = new List<string>();

        if (!string.IsNullOrWhiteSpace(config.Value.FileSystemRoot))
        {
            candidates.Add(Path.GetFullPath(Path.Combine(config.Value.FileSystemRoot, relativePath)));
        }

        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var directory = new DirectoryInfo(start);
            for (var depth = 0; directory is not null && depth < 10; depth++, directory = directory.Parent)
            {
                candidates.Add(Path.Combine(directory.FullName, relativePath));
            }
        }

        return candidates
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(File.Exists);
    }

    private static void AddManifest(
        Dictionary<string, SkillCatalogEntry> entries,
        string plugin,
        string manifestPath,
        string? query)
    {
        var content = File.ReadAllText(manifestPath);
        var name = NamePattern.Match(content).Groups[1].Value.Trim();
        if (string.IsNullOrWhiteSpace(name)) return;

        var description = DescriptionPattern.Match(content).Groups[1].Value.Trim();
        if (!string.IsNullOrWhiteSpace(query) &&
            !$"{name} {description}".Contains(query, StringComparison.OrdinalIgnoreCase)) return;

        entries.TryAdd(name, new SkillCatalogEntry(name, plugin, description));
    }
}

public sealed record SkillCatalogSnapshot(
    string Source,
    int Count,
    int Plugins,
    IReadOnlyList<SkillCatalogEntry> Skills);

public sealed record SkillCatalogEntry(string Name, string Plugin, string Description);
