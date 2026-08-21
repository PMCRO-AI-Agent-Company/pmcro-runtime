// src/ProjectName.OrchestratorApi/Services/SkillCatalogService.cs
// Read-only HTTP-facing catalog over the real on-disk skills/ tree that makes
// up the PMCR-O Colony (C-Suite domain skills, orchestrator-agent, and the
// tool-agent skills). This is a DIFFERENT surface than MAF's AgentSkillsProvider
// inside OrchestratorService: that one is an in-process context provider the
// LLM itself calls (advertise/load_skill/read_skill_resource) during a cycle.
// Nothing before this exposed the catalog over HTTP for a human, the frontend,
// or an external tool (Cline, etc.) to browse independent of a running cycle.
// Never fabricates content — reads exactly what is on disk under
// <FileSystemRoot>/skills, same root FileTrailWriter/TrailReader already use.

using System.Collections.Frozen;
using Microsoft.Extensions.Options;
using ProjectName.OrchestratorService.Configuration;

namespace ProjectName.OrchestratorApi.Services;

public sealed record SkillSummary(
    string Name,
    string Description,
    string Version,
    string RelativePath,
    bool HasCommands,
    bool HasReferences,
    bool HasScripts,
    bool HasAssets);

public sealed record SkillDetail(
    SkillSummary Summary,
    string RawManifest,
    IReadOnlyList<string> CommandFiles,
    IReadOnlyList<string> ReferenceFiles,
    IReadOnlyList<string> ScriptFiles);

public sealed class SkillCatalogService(IOptions<OrchestratorConfig> orchestratorConfig, ILogger<SkillCatalogService> logger)
{
    private string SkillsRoot => Path.Combine(orchestratorConfig.Value.FileSystemRoot, "skills");

    public IReadOnlyList<SkillSummary> ListSkills()
    {
        var root = SkillsRoot;
        if (!Directory.Exists(root))
        {
            logger.LogWarning("[SkillCatalog] skills root not found: {Root}", root);
            return Array.Empty<SkillSummary>();
        }

        var summaries = new List<SkillSummary>();
        foreach (var skillMdPath in Directory.EnumerateFiles(root, "SKILL.md", SearchOption.AllDirectories))
        {
            summaries.Add(BuildSummary(root, skillMdPath));
        }
        return summaries.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public SkillDetail? GetSkill(string name)
    {
        var match = ListSkills().FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
        if (match is null) return null;

        var folder = Path.Combine(SkillsRoot, match.RelativePath);
        var skillMdPath = Path.Combine(folder, "SKILL.md");
        var rawManifest = File.Exists(skillMdPath) ? File.ReadAllText(skillMdPath) : string.Empty;

        return new SkillDetail(
            match,
            rawManifest,
            ListFiles(Path.Combine(folder, "commands")),
            ListFiles(Path.Combine(folder, "references")),
            ListFiles(Path.Combine(folder, "scripts")));
    }

    private static SkillSummary BuildSummary(string skillsRoot, string skillMdPath)
    {
        var folder = Path.GetDirectoryName(skillMdPath)!;
        var relativePath = Path.GetRelativePath(skillsRoot, folder);
        var text = File.ReadAllText(skillMdPath);

        var declaredName = ExtractYamlField(text, "name");
        var name = !string.IsNullOrWhiteSpace(declaredName) ? declaredName! : Path.GetFileName(folder);
        var description = ExtractYamlField(text, "description") ?? string.Empty;
        var version = ExtractYamlField(text, "version") ?? "1.0.0";

        return new SkillSummary(
            name,
            description,
            version,
            relativePath,
            Directory.Exists(Path.Combine(folder, "commands")),
            Directory.Exists(Path.Combine(folder, "references")),
            Directory.Exists(Path.Combine(folder, "scripts")),
            Directory.Exists(Path.Combine(folder, "assets")));
    }

    private static IReadOnlyList<string> ListFiles(string dir) =>
        Directory.Exists(dir)
            ? Directory.EnumerateFiles(dir).Select(Path.GetFileName).OfType<string>().OrderBy(f => f).ToList()
            : Array.Empty<string>();

    // Same lightweight frontmatter extraction PmcroSkillLoader already uses
    // elsewhere in this codebase — no YAML library dependency for two fields.
    private static string? ExtractYamlField(string text, string field)
    {
        var lines = text.Split('\n');
        var fieldLine = lines.FirstOrDefault(l => l.Trim().StartsWith($"{field}:", StringComparison.OrdinalIgnoreCase));
        return fieldLine?.Split(':').LastOrDefault()?.Trim(' ', '"', '\'', '\r');
    }
}
