// src/services/ProjectName.OrchestratorService/Skills/SkillManifestReader.cs
// ARCH-NATIVE-MAF-001 (2026-07-20): Thin adapter for reading SKILL.md files
// directly from the marketplace source tree. This replaces the redundant
// PmcroSkillLoader by only handling the Colony Laws extraction needed for
// subject agent instructions, while MAF's native AgentSkillsProvider handles
// all other skill lifecycle (advertise, load_skill, read_skill_resource, run_skill_script).
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProjectName.OrchestratorService.Configuration;

namespace ProjectName.OrchestratorService.Skills;

/// <summary>
/// Reads skill manifest content directly from marketplace source paths.
/// Used ONLY by subject agents to extract their Colony Laws section for
/// instruction composition. All other skill operations are handled by
/// MAF's native AgentSkillsProvider via MarketplaceSkillsMaterializer's
/// materialized StagingRoot.
/// </summary>
public sealed class SkillManifestReader(
    ILogger<SkillManifestReader> logger,
    IOptions<OrchestratorConfig> config)
{
    // Resolves the source path for a skill name from marketplace.json
    public string? ResolveSkillPath(string skillName)
    {
        var repoRoot = config.Value.FileSystemRoot;
        var marketplacePath = Path.Combine(repoRoot, ".agents/plugins/marketplace.json".Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(marketplacePath))
            return null;

        try
        {
            var json = File.ReadAllText(marketplacePath);
            using var doc = JsonDocument.Parse(json);

            foreach (var plugin in doc.RootElement.GetProperty("plugins").EnumerateArray())
            {
                var source = plugin.GetProperty("source").GetString();
                var pluginRoot = Path.GetFullPath(Path.Combine(repoRoot, source!));
                var skillPath = Path.Combine(pluginRoot, "skills", skillName, "SKILL.md");

                if (File.Exists(skillPath))
                    return skillPath;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[SkillManifestReader] Failed to resolve skill path for {SkillName}", skillName);
        }

        return null;
    }

    /// <summary>
    /// Reads the Colony Laws section from a skill manifest.
    /// This is the ONLY purpose of this class -- replacing PmcroSkillLoader's
    /// redundant full-manifest loading.
    /// </summary>
    public string? ReadColonyLaws(string skillName)
    {
        var path = ResolveSkillPath(skillName);
        if (path is null)
            return null;

        try
        {
            var content = File.ReadAllText(path);
            return ExtractColonyLaws(content);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[SkillManifestReader] Failed to read Colony Laws for {SkillName}", skillName);
            return null;
        }
    }

    private static string? ExtractColonyLaws(string manifest)
    {
        const string startMarker = "## Colony Laws";
        const string endMarker = "## Skill Package Layout";

        var start = manifest.IndexOf(startMarker, StringComparison.Ordinal);
        if (start < 0)
            return null;

        var end = manifest.IndexOf(endMarker, start, StringComparison.Ordinal);
        return (end > start ? manifest[start..end] : manifest[start..]).Trim();
    }
}