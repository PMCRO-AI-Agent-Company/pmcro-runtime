// Skills/SkillManifestReader.cs
// Reads subject-agent SKILL.md colony-law sections for Checker compliance scoring.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProjectName.OrchestratorService.Configuration;

namespace ProjectName.OrchestratorService.Skills;

public sealed class SkillManifestReader
{
    private readonly OrchestratorConfig _config;
    private readonly ILogger<SkillManifestReader> _logger;

    public SkillManifestReader(IOptions<OrchestratorConfig> config, ILogger<SkillManifestReader> logger)
    {
        _config = config.Value;
        _logger = logger;
    }

    /// <summary>
    /// Extract the "## Colony Laws" section from a subject agent's SKILL.md
    /// under .pmcro/skills/ or a configured skill root. Returns null if missing.
    /// </summary>
    public string? ReadColonyLaws(string subjectAgentName)
    {
        try
        {
            var roots = new[]
            {
                Path.Combine(_config.FileSystemRoot ?? "Z:\\pmcro-runtime", ".pmcro", "skills"),
                Path.Combine(AppContext.BaseDirectory, ".pmcro", "skills"),
                Path.Combine(Directory.GetCurrentDirectory(), ".pmcro", "skills")
            };

            foreach (var root in roots)
            {
                if (!Directory.Exists(root)) continue;

                // Prefer exact folder match, then any SKILL.md that mentions the agent
                var candidate = Path.Combine(root, subjectAgentName, "SKILL.md");
                if (!File.Exists(candidate))
                {
                    candidate = Directory.EnumerateFiles(root, "SKILL.md", SearchOption.AllDirectories)
                        .FirstOrDefault(p => p.Contains(subjectAgentName, StringComparison.OrdinalIgnoreCase))
                        ?? string.Empty;
                }

                if (string.IsNullOrEmpty(candidate) || !File.Exists(candidate)) continue;

                var text = File.ReadAllText(candidate);
                return ExtractColonyLawsSection(text);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SkillManifestReader failed for {Agent}", subjectAgentName);
        }

        return null;
    }

    private static string? ExtractColonyLawsSection(string markdown)
    {
        const string marker = "## Colony Laws";
        var idx = markdown.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;

        var start = idx + marker.Length;
        var next = markdown.IndexOf("\n## ", start, StringComparison.Ordinal);
        var section = next < 0 ? markdown[start..] : markdown[start..next];
        return section.Trim();
    }
}
