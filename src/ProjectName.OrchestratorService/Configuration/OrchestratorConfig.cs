// Configuration/OrchestratorConfig.cs
// PMCR-O Orchestrator runtime configuration.
// Every value sourced from appsettings.json / environment — no hardcoded paths or limits.

namespace ProjectName.OrchestratorService.Configuration;

public sealed class OrchestratorConfig
{
    public const string SectionName = "Orchestrator";

    /// <summary>Root path of the PMCR-O filesystem (skills/, .pmcro/, trails/).</summary>
    public required string FileSystemRoot { get; set; }

    /// <summary>Trail root relative to FileSystemRoot.</summary>
    public string TrailRoot { get; set; } = ".pmcro/trails/orchestrator";

    /// <summary>EC-009: maximum cognitive loop iterations before forced HALT.</summary>
    public int MaxLoops { get; set; } = 5;

    /// <summary>Ollama model identifier for the orchestrator.</summary>
    public string ModelId { get; set; } = "qwen3:8b";

    /// <summary>Checker pass threshold (0.0–1.0).</summary>
    public double CheckerPassThreshold { get; set; } = 0.80;
}
