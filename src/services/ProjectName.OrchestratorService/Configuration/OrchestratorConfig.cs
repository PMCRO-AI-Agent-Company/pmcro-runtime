namespace ProjectName.OrchestratorService.Configuration;

/// <summary>
/// Orchestrator runtime configuration.
/// GTDDD-MANDATE: every value here is sourced from appsettings.json / environment —
/// no hardcoded paths or limits in code.
/// </summary>
public sealed class OrchestratorConfig
{
    public const string SectionName = "Orchestrator";

    /// <summary>Root path of the PMCR-O filesystem (skills/, .pmcro/, trails, etc.)</summary>
    public required string FileSystemRoot { get; set; }

    /// <summary>Trail root relative to FileSystemRoot (.pmcro/trails/orchestrator)</summary>
    public string TrailRoot { get; set; } = ".pmcro/trails/orchestrator";

    /// <summary>EC-009: maximum cognitive loop iterations before forced HALT.</summary>
    public int MaxLoops { get; set; } = 3;

    /// <summary>Ollama model identifier bound to this orchestrator instance.</summary>
    public string ModelId { get; set; } = "qwen3:8b";

    /// <summary>Enable meta-layer cascade when trails pass but have meta-errors.</summary>
    public bool TrailChainMode { get; set; } = true;

    /// <summary>Enable seed intent synthesis from disposition files.</summary>
    public bool SeedIntentSynthesis { get; set; } = true;

    /// <summary>
    /// NIGHT SHIFT / SUCCESSION LAW safety cap: maximum number of trails a single
    /// autonomous chain (fire-and-forget /api/cycle triggering the next trail from
    /// each sealed disposition's NextSeedIntent) will run before stopping on its own,
    /// even if the Reflector keeps handing off a Baton. There is no Economic Governor
    /// in this codebase (checked — it does not exist as a real concept anywhere else),
    /// so this is a blunt, deliberately dumb backstop against a runaway chain, not a
    /// cost/value judgement. TYPE1 actions still hit the real HIL gate every cycle
    /// regardless of this cap (see DevUiHilChannel — DEV-GODMODE-001 is disabled).
    /// </summary>
    public int MaxChainedTrails { get; set; } = 20;

    /// <summary>
    /// Marketplace index JSON relative path (mirrored to .claude-plugin/marketplace.json).
    /// </summary>
    public string MarketplaceRelativePath { get; set; } = ".agents/plugins/marketplace.json";

    /// <summary>
    /// Skills staging root relative to FileSystemRoot where AgentSkillsProvider reads.
    /// Materializes skills from marketplace.json plugins into this directory.
    /// </summary>
    public string SkillsStagingPath { get; set; } = ".pmcro/skills-staging";
}

// McpEndpointsConfig removed 2026-06-20: unused dead config. McpToolCache resolves
// mcp-filesystem/mcp-playwright/mcp-terminal by service name via Aspire service
// discovery (.AddServiceDiscovery() in Program.cs), not from these hardcoded URLs.
// The fixed ports here (7031/7032/7033) had drifted from the real Aspire-assigned
// ports and were never actually read by any code path.