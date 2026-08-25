using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProjectName.OrchestratorService.Configuration;

namespace ProjectName.OrchestratorService.Skills;

/// <summary>
/// Thin PMCRO adapter that reads only the Colony Laws section needed for
/// subject-agent identity/governance instructions. MAF's AgentSkillsProvider
/// remains responsible for progressive skill discovery and resource/script tools.
/// </summary>
public sealed class SkillManifestReader(
    ILogger<SkillManifestReader> logger,
    IOptions<OrchestratorConfig> config)
{
    public string? ResolveSkillPath(string skillName)
    {
        foreach (var configuredPath in config.Value.SkillPaths ?? [])
        {
            if (string.IsNullOrWhiteSpace(configuredPath))
                continue;

            var sourceRoot = Path.IsPathRooted(configuredPath)
                ? Path.GetFullPath(configuredPath)
                : Path.GetFullPath(Path.Combine(
                    config.Value.FileSystemRoot,
                    configuredPath.Replace('/', Path.DirectorySeparatorChar)));

            if (!Directory.Exists(sourceRoot))
                continue;

            var direct = Path.Combine(sourceRoot, skillName, "SKILL.md");
            if (File.Exists(direct))
                return direct;

            var discovered = Directory.EnumerateFiles(sourceRoot, "SKILL.md", SearchOption.AllDirectories)
                .FirstOrDefault(path => string.Equals(
                    Path.GetFileName(Path.GetDirectoryName(path)),
                    skillName,
                    StringComparison.OrdinalIgnoreCase));

            if (discovered is not null)
                return discovered;
        }

        return null;
    }

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
            logger.LogWarning(ex,
                "[SkillManifestReader] Failed to read Colony Laws for {SkillName}",
                skillName);
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
