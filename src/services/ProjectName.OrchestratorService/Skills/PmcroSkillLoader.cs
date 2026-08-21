// src/services/ProjectName.OrchestratorService/Skills/PmcroSkillLoader.cs
using System.Collections.Frozen;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace ProjectName.OrchestratorService.Skills;

/// <summary>
/// Represents the full skill manifest including the original frontmatter 
/// to ensure the Orchestrator LLM has full visibility into capability classes and laws.
/// </summary>
public sealed record AgentSkill(
    string AgentName,
    string Description,
    string Version,
    string FullManifest, // Contains the entire substituted SKILL.md (YAML + Markdown)
    FrozenDictionary<string, string> Commands
);

public sealed class PmcroSkillLoader(ILogger<PmcroSkillLoader> logger)
{
    private FrozenDictionary<string, AgentSkill> _skills = FrozenDictionary<string, AgentSkill>.Empty;
    private Dictionary<string, string> _identity = [];

    /// <summary>
    /// Lists all advertised skills in the Colony.
    /// </summary>
    public IReadOnlyList<string> Advertise() =>
        _skills.Values.Select(s => $"{s.AgentName}: {s.Description}").ToList();

    /// <summary>
    /// Retrieves a specific skill by agent name.
    /// </summary>
    public AgentSkill? GetSkill(string name) =>
        _skills.TryGetValue(name, out var s) ? s : null;

    /// <summary>
    /// Returns all registered skills.
    /// </summary>
    public IEnumerable<AgentSkill> GetAllSkills() => _skills.Values;

    /// <summary>
    /// ARCH-SKILLLOADER-DISK-001 (2026-07-20): Bootstraps the skill registry by
    /// scanning the real on-disk skills tree instead of compile-time embedded
    /// manifest resources. Previously this method called
    /// Assembly.GetExecutingAssembly().GetManifestResourceNames() -- a completely
    /// separate mechanism from MarketplaceSkillsMaterializer's StagingRoot, which
    /// writes real files to disk at runtime from marketplace.json. No amount of
    /// fixing the (also broken, and since-removed) build-time EmbeddedResource
    /// glob would have connected the two: embedding happens at compile time,
    /// before the materializer ever runs, so GetManifestResourceNames() would
    /// always be empty regardless of glob correctness. Confirmed via live log:
    /// right after the materializer fix (cycle a7c1e920), "[MarketplaceSkills]
    /// Materialized 17 skill(s)..." logged successfully, immediately followed by
    /// "[SkillLoader] Bootstrap complete. 0 agents recognized." -- proving these
    /// were two disconnected systems. Fix: walk skillsRoot (the same StagingRoot
    /// the materializer populates) for every SKILL.md, and read commands/*.md
    /// from each skill folder's own commands/ subfolder (already mirrored there
    /// by MarketplaceSkillsMaterializer.MaterializeAsync's sibling-folding step)
    /// -- no resource-name string surgery needed since real folder structure
    /// exists on disk.
    /// </summary>
    public void LoadAll(string skillsRoot, string? identityJsonPath = null)
    {
        _identity = LoadIdentity(identityJsonPath);
        var dict = new Dictionary<string, AgentSkill>(StringComparer.OrdinalIgnoreCase);

        if (!Directory.Exists(skillsRoot))
        {
            logger.LogWarning("[SkillLoader] skills root not found: {Root} -- 0 agents will be recognized.", skillsRoot);
            _skills = dict.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
            logger.LogInformation("[SkillLoader] Bootstrap complete. {Count} agents recognized.", _skills.Count);
            return;
        }

        foreach (var skillMdPath in Directory.EnumerateFiles(skillsRoot, "SKILL.md", SearchOption.AllDirectories))
        {
            var rawText = File.ReadAllText(skillMdPath);
            var subText = SubstituteTokens(rawText, _identity);

            // BUG-SKILLNAME-001 (2026-07-11) still applies in spirit: trust the
            // SKILL.md frontmatter's own `name:` field as the primary source of
            // truth (authored hyphenated, e.g. "filesystem-agent", matching how
            // Program.cs registers/looks up agents). Only fall back to the
            // skill's own folder name if a skill ships without a `name:` field.
            var declaredName = ExtractYamlField(subText, "name");
            var agentName = !string.IsNullOrWhiteSpace(declaredName)
                ? declaredName
                : Path.GetFileName(Path.GetDirectoryName(skillMdPath)!);

            var description = ExtractYamlField(subText, "description") ?? "PMCRO Phase Agent";
            var version = ExtractYamlField(subText, "version") ?? "1.0.0";

            // Load Commands from this skill folder's own commands/ subfolder --
            // MarketplaceSkillsMaterializer already mirrors the plugin-root-level
            // commands/ sibling into <target>/commands/ for exactly this reason.
            var commandDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var skillFolder = Path.GetDirectoryName(skillMdPath)!;
            var commandsDir = Path.Combine(skillFolder, "commands");
            if (Directory.Exists(commandsDir))
            {
                foreach (var cmdFile in Directory.EnumerateFiles(commandsDir, "*.md"))
                {
                    var commandKey = Path.GetFileNameWithoutExtension(cmdFile);
                    commandDict[commandKey] = SubstituteTokens(File.ReadAllText(cmdFile), _identity);
                }
            }

            // Storing the entire subText (FullManifest) to keep YAML frontmatter visible to the LLM
            dict[agentName] = new AgentSkill(agentName, description, version, subText, commandDict.ToFrozenDictionary());

            logger.LogInformation("[SkillLoader] Fully indexed agent: {Agent}", agentName);
        }

        _skills = dict.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        logger.LogInformation("[SkillLoader] Bootstrap complete. {Count} agents recognized.", _skills.Count);
    }

    private static string? ExtractYamlField(string text, string field)
    {
        var lines = text.Split('\n');
        var fieldLine = lines.FirstOrDefault(l => l.Trim().StartsWith($"{field}:", StringComparison.OrdinalIgnoreCase));
        return fieldLine?.Split(':').LastOrDefault()?.Trim(' ', '"', '\'', '\r');
    }

    private static string SubstituteTokens(string text, Dictionary<string, string> tokens)
    {
        foreach (var (k, v) in tokens) text = text.Replace($"{{{{{k}}}}}", v, StringComparison.OrdinalIgnoreCase);
        return text;
    }

    private static Dictionary<string, string> LoadIdentity(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return new();
        try { return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? new(); }
        catch { return new(); }
    }
}
