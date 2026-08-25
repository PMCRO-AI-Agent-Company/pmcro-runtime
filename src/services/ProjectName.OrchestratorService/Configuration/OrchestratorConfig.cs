namespace ProjectName.OrchestratorService.Configuration;

/// <summary>
/// Orchestrator runtime configuration.
/// GTDDD-MANDATE: runtime values are sourced from configuration/environment;
/// repository-local skill ownership is not assumed by the service.
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

    /// <summary>Marketplace metadata used for catalog/browsing, not live skill ownership.</summary>
    public string MarketplaceRelativePath { get; set; } = ".agents/plugins/marketplace.json";

    /// <summary>
    /// Canonical Agent Skills roots. Paths are resolved relative to FileSystemRoot
    /// unless absolute. The default is the sibling pmcro-skills repository.
    /// </summary>
    public string[] SkillPaths { get; set; } = ["../pmcro-skills/.agents/skills"];

    /// <summary>Generated/cache root consumed by MAF's AgentSkillsProvider.</summary>
    public string SkillsStagingPath { get; set; } = ".pmcro/skills-staging";
}

// MCP endpoints are resolved through Aspire service discovery; no fixed ports belong here.
