// src/ProjectName.OrchestratorService/PmcroLoop.cs
// PMCR-O Cognitive Loop — SEQUENTIAL-001 dispatch
// Ollama wiring deferred to runtime — IChatClient API stabilizes with .NET 11 GA

namespace ProjectName.OrchestratorService;

public class PmcroLoop
{
    private readonly IConfiguration _config;
    public PmcroLoop(IConfiguration config) => _config = config;
    public int MaxLoops => _config.GetValue("MaxLoops", 5);
}

public class McpToolCache { }