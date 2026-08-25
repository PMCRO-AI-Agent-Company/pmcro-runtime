namespace ProjectName.OrchestratorService.Configuration;

/// <summary>
/// Orchestrator runtime configuration.
/// Runtime values are sourced from configuration/environment; repository-local
/// skill ownership is not assumed by the service.
/// </summary>
public sealed class OrchestratorConfig
{
    public const string SectionName = "Orchestrator";

    public required string FileSystemRoot { get; set; }
    public string TrailRoot { get; set; } = ".pmcro/trails/orchestrator";
    public int MaxLoops { get; set; } = 3;
    public string ModelId { get; set; } = "qwen3:8b";
    public bool TrailChainMode { get; set; } = true;
    public bool SeedIntentSynthesis { get; set; } = true;
    public int MaxChainedTrails { get; set; } = 20;

    /// <summary>Marketplace metadata used for catalog/browsing, not runtime state.</summary>
    public string MarketplaceRelativePath { get; set; } = ".agents/plugins/marketplace.json";

    /// <summary>
    /// Canonical skill roots. These are capability sources; runtime state remains
    /// under .pmcro. Keep the list curated because MAF deduplicates duplicate skill
    /// names with first-source precedence.
    /// </summary>
    public string[] SkillPaths { get; set; } =
    [
        "../pmcro-skills/plugins/pmcro-maf/skills",
        "../pmcro-skills/plugins/pmcro-orchestrator/skills",
        "../pmcro-skills/plugins/pmcro-strategy/skills",
        "../dotnet-skills/plugins/dotnet-maf/skills",
        "../github-skills/plugins/github/skills",
        "../figma-skills/plugins/figma/skills"
    ];

    /// <summary>Generated/cache root consumed by MAF's AgentSkillsProvider.</summary>
    public string SkillsStagingPath { get; set; } = ".pmcro/skills-staging";
}

// MCP endpoints are resolved through Aspire service discovery; no fixed ports belong here.
