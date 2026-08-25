using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProjectName.OrchestratorService.Configuration;

namespace ProjectName.OrchestratorService.Skills;

/// <summary>
/// Resolves the configured Agent Skills repositories into the generated staging
/// directory consumed by MAF's AgentSkillsProvider.
///
/// The canonical PMCRO source is ../pmcro-skills/.agents/skills. Marketplace
/// metadata remains a catalog concern; it is not the runtime skill source.
/// This adapter exists only because the runtime's MAF providers currently point
/// at a stable generated directory and the source repository can live outside
/// the runtime repository.
/// </summary>
public sealed class MarketplaceSkillsMaterializer(
    ILogger<MarketplaceSkillsMaterializer> logger,
    IOptions<OrchestratorConfig> config)
{
    public string StagingRoot { get; } = GetStagingRoot(config.Value);

    private static string GetStagingRoot(OrchestratorConfig config)
    {
        if (Path.IsPathRooted(config.SkillsStagingPath))
            return Path.GetFullPath(config.SkillsStagingPath);

        return Path.GetFullPath(Path.Combine(
            config.FileSystemRoot,
            config.SkillsStagingPath.Replace('/', Path.DirectorySeparatorChar)));
    }

    /// <summary>
    /// Materializes every configured skill source into a single MAF-compatible
    /// root. A source is expected to contain skill directories with SKILL.md.
    /// The source directories themselves remain the source of truth.
    /// </summary>
    public Task<int> MaterializeAsync(CancellationToken ct = default)
    {
        var skillPaths = config.Value.SkillPaths ?? [];
        Directory.CreateDirectory(StagingRoot);

        var count = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var configuredPath in skillPaths)
        {
            ct.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(configuredPath))
                continue;

            var sourceRoot = Path.IsPathRooted(configuredPath)
                ? Path.GetFullPath(configuredPath)
                : Path.GetFullPath(Path.Combine(
                    config.Value.FileSystemRoot,
                    configuredPath.Replace('/', Path.DirectorySeparatorChar)));

            if (!Directory.Exists(sourceRoot))
            {
                logger.LogWarning("[MarketplaceSkills] configured skill source not found: {SourceRoot}", sourceRoot);
                continue;
            }

            foreach (var skillMd in Directory.EnumerateFiles(sourceRoot, "SKILL.md", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();

                var skillDir = Path.GetDirectoryName(skillMd)!;
                var skillName = Path.GetFileName(skillDir);
                if (string.IsNullOrWhiteSpace(skillName))
                    continue;

                // Prevent duplicate skill names from competing across sources.
                // The first configured source wins, matching the documented
                // first-source precedence we want for PMCRO skill resolution.
                if (!seen.Add(skillName))
                    continue;

                var target = Path.Combine(StagingRoot, skillName);
                MirrorDirectory(skillDir, target);
                count++;
            }
        }

        logger.LogInformation(
            "[MarketplaceSkills] Materialized {Count} skill(s) into {StagingRoot} from {SourceCount} configured source(s).",
            count, StagingRoot, skillPaths.Length);

        return Task.FromResult(count);
    }

    private static void MirrorDirectory(string src, string dest)
    {
        Directory.CreateDirectory(dest);

        foreach (var file in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(src, file);
            var destFile = Path.Combine(dest, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);
            File.Copy(file, destFile, overwrite: true);
        }
    }
}
