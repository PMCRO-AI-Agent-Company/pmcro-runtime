// Services/McpToolCache.cs
// MCP tool cache — read tools for Checker + execution report helpers.
// Stub implementation sufficient for compile + sequential loop; real MCP wiring
// attaches when the three MCP projects implement ModelContextProtocol servers.

using Microsoft.Extensions.AI;

namespace ProjectName.OrchestratorService.Services;

public sealed class McpToolCache
{
    private readonly List<AITool> _readTools = [];
    private readonly Dictionary<string, Queue<string>> _captured = new(StringComparer.OrdinalIgnoreCase);

    public IList<AITool> GetReadTools() => _readTools;

    public void RegisterReadTool(AITool tool) => _readTools.Add(tool);

    /// <summary>
    /// True when the maker artifact contains a structured MCP execution report
    /// (not a pending/HIL stub). Used by the split-turn path to decide whether
    /// to synthesize from cache vs use raw LLM text.
    /// </summary>
    public static bool HasExecutionReport(string? artifact)
    {
        if (string.IsNullOrWhiteSpace(artifact)) return false;
        return artifact.Contains("\"tool_calls\"", StringComparison.OrdinalIgnoreCase)
            || artifact.Contains("\"execution_report\"", StringComparison.OrdinalIgnoreCase)
            || artifact.Contains("\"ok\":", StringComparison.OrdinalIgnoreCase);
    }

    public void DrainCapturedResults(string agentKey)
    {
        if (_captured.TryGetValue(agentKey, out var q))
            q.Clear();
    }

    public void Capture(string agentKey, string result)
    {
        if (!_captured.TryGetValue(agentKey, out var q))
        {
            q = new Queue<string>();
            _captured[agentKey] = q;
        }
        q.Enqueue(result);
    }

    /// <summary>
    /// Terminal preflight: resolve whether a command exists on PATH.
    /// Returns a descriptive string for PREFLIGHT_CONTEXT injection.
    /// </summary>
    public Task<string> WhichPreflight(string command)
    {
        // Without a live terminal MCP, report unresolved so Planner can still proceed.
        return Task.FromResult($"(unresolved — MCP terminal not connected) command='{command}'");
    }
}
