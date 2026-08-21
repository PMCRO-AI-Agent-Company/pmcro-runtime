// src/services/ProjectName.OrchestratorService/Skills/MarketplaceSkillsMaterializer.cs
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProjectName.OrchestratorService.Configuration;

namespace ProjectName.OrchestratorService.Skills;

/// <summary>
/// ARCH-MARKETPLACE-BRIDGE-001 (2026-07-20): bridges this repo's Anthropic-style
/// marketplace (.agents/plugins/marketplace.json) to MAF's native AgentSkillsProvider.
///
/// Program.cs previously pointed AgentSkillsProvider at a single hardcoded
/// directory, Path.Combine(AppContext.BaseDirectory, "skills"), populated by a
/// build-time csproj Content glob (`..\..\..\skills\**`). That glob resolved
/// against a `skills/` folder at the repo root -- which does not exist; this
/// repo's skills live under `catalog/Platform/PMCR-O/skills/*` and
/// `catalog/Tools/AI-Company/skills/*`, per the marketplace.json `source` fields.
/// The glob therefore silently matched zero files (MSBuild globs against a
/// missing directory produce an empty item list, not an error), so
/// AgentSkillsProvider was reading an empty directory at runtime -- confirmed by
/// checking for `&lt;FileSystemRoot&gt;\skills` directly (ENOENT).
///
/// This class replaces that dead build-time copy with a runtime materializer:
/// read marketplace.json, resolve each plugin's `source` to an absolute path,
/// find every `&lt;pluginRoot&gt;/skills/&lt;skill-name&gt;/SKILL.md`, and mirror each skill
/// folder into StagingRoot -- the same physical directory AgentSkillsProvider
/// already reads from, so Program.cs's construction of AgentSkillsProvider itself
/// barely changes (see Program.cs, `sp.GetRequiredService&lt;MarketplaceSkillsMaterializer&gt;().StagingRoot`).
///
/// marketplace.json becomes the single discovery index; this class is the "minimal
/// adapter layer" between it and MAF's native skill loading -- it does not
/// reimplement load_skill/read_skill_resource/run_skill_script, it only makes sure
/// the files those MAF-native tools expect are on disk where MAF looks for them.
/// </summary>
public sealed class MarketplaceSkillsMaterializer(
    ILogger<MarketplaceSkillsMaterializer> logger,
    IOptions<OrchestratorConfig> config)
{
    /// <summary>
    /// Directory AgentSkillsProvider reads from. Resolved from OrchestratorConfig.SkillsStagingPath
    /// relative to FileSystemRoot, or absolute path if the config value is already absolute.
    /// Same physical path the old build-time Content-copy ItemGroup used to populate (see .csproj) -- now
    /// populated at runtime from marketplace.json instead of a static, broken glob.
    /// </summary>
    public string StagingRoot { get; } = GetStagingRoot(config.Value);

    private static string GetStagingRoot(OrchestratorConfig config)
    {
        var stagingPath = config.SkillsStagingPath;
        var root = config.FileSystemRoot;

        // Handle both relative and absolute paths
        if (Path.IsPathRooted(stagingPath))
        {
            return Path.GetFullPath(stagingPath);
        }

        return Path.GetFullPath(Path.Combine(root, stagingPath.Replace('/', Path.DirectorySeparatorChar)));
    }

    /// <summary>
    /// Reads marketplace.json and mirrors every resolvable plugin's skill folder(s)
    /// into StagingRoot. Safe to call repeatedly (idempotent overwrite-on-copy) --
    /// this is what makes hot-loading work: see MarketplaceSkillsWatcherService,
    /// which re-invokes this on every marketplace.json change.
    /// </summary>
    public async Task<int> MaterializeAsync(CancellationToken ct = default)
    {
        var repoRoot = config.Value.FileSystemRoot;
        var marketplacePath = Path.Combine(repoRoot,
            config.Value.MarketplaceRelativePath.Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(marketplacePath))
        {
            logger.LogWarning(
                "[MarketplaceSkills] marketplace.json not found at {Path} -- skipping materialization.",
                marketplacePath);
            return 0;
        }

        JsonDocument doc;
        try
        {
            var text = await File.ReadAllTextAsync(marketplacePath, ct);
            doc = JsonDocument.Parse(text);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[MarketplaceSkills] failed to parse {Path}", marketplacePath);
            return 0;
        }

        if (!doc.RootElement.TryGetProperty("plugins", out var plugins) || plugins.ValueKind != JsonValueKind.Array)
        {
            logger.LogWarning("[MarketplaceSkills] {Path} has no 'plugins' array.", marketplacePath);
            return 0;
        }

        Directory.CreateDirectory(StagingRoot);
        var materializedCount = 0;

        foreach (var plugin in plugins.EnumerateArray())
        {
            var name = plugin.TryGetProperty("name", out var n) ? n.GetString() : null;
            var source = plugin.TryGetProperty("source", out var s) ? s.GetString() : null;
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(source))
            {
                logger.LogWarning("[MarketplaceSkills] plugin entry missing name/source, skipping: {Entry}",
                    plugin.GetRawText());
                continue;
            }

            var pluginRoot = Path.GetFullPath(Path.Combine(repoRoot, source));
            if (!Directory.Exists(pluginRoot))
            {
                logger.LogWarning("[MarketplaceSkills] plugin '{Name}' source not found on disk: {PluginRoot}",
                    name, pluginRoot);
                continue;
            }

            // Every plugin package in this repo bundles its skill(s) under
            // <pluginRoot>/skills/<skill-name>/SKILL.md (see catalog/Platform/PMCR-O
            // and catalog/Tools/AI-Company). A plugin with no skills/ subtree is a
            // packaging defect worth a warning, not a reason to fail the whole pass.
            var skillsDir = Path.Combine(pluginRoot, "skills");
            if (!Directory.Exists(skillsDir))
            {
                logger.LogWarning(
                    "[MarketplaceSkills] plugin '{Name}' has no skills/ subdirectory at {PluginRoot} -- skipping.",
                    name, pluginRoot);
                continue;
            }

            foreach (var skillDir in Directory.EnumerateDirectories(skillsDir))
            {
                var skillMd = Path.Combine(skillDir, "SKILL.md");
                if (!File.Exists(skillMd)) continue;

                var skillName = Path.GetFileName(skillDir);
                var target = Path.Combine(StagingRoot, name, skillName);
                MirrorDirectory(skillDir, target);

                // This repo keeps commands/, references/, scripts/ as siblings of
                // skills/<name>/ at the PLUGIN root (e.g.
                // catalog/Platform/PMCR-O/skills/orchestrator/{commands,references,scripts}),
                // not nested inside the skill's own folder the way the July 2026 Agent
                // Skills GA convention expects (SKILL.md + references/ + assets/ +
                // scripts/ all inside one skill folder). Fold each plugin-level sibling
                // into the materialized skill folder so MAF's load_skill /
                // read_skill_resource / run_skill_script find them where the GA spec
                // expects, without physically restructuring every existing package.
                // "agents" covers the domain plugins' persona files (e.g.
                // catalog/Tools/AI-Company/skills/cto/agents/software-engineer.md) --
                // same sibling-of-plugin-root layout as commands/references/scripts.
                foreach (var sibling in new[] { "commands", "references", "scripts", "assets", "agents" })
                {
                    var siblingSrc = Path.Combine(pluginRoot, sibling);
                    if (Directory.Exists(siblingSrc))
                        MirrorDirectory(siblingSrc, Path.Combine(target, sibling));
                }

                materializedCount++;
            }
        }

        logger.LogInformation(
            "[MarketplaceSkills] Materialized {Count} skill(s) from {Path} into {StagingRoot}.",
            materializedCount, marketplacePath, StagingRoot);
        return materializedCount;
    }

    // Mirrors src into dest, overwriting on every pass so an updated marketplace
    // package (new SKILL.md revision, new script) is picked up on the next
    // MaterializeAsync() call. Deliberately does NOT delete dest files that no
    // longer exist in src -- a stale leftover file is a smaller failure mode than
    // a watcher-triggered pass racing a partial read/delete mid-copy.
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