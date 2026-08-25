using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using System.Collections.Concurrent;

namespace ProjectName.OrchestratorService.Services;

/// <summary>
/// Native MCP client boundary for PMCRO subject agents.
/// MCP transport/protocol handling belongs to the official MCP C# SDK;
/// PMCRO remains responsible for routing, policy, HIL, trails and governance.
/// </summary>
public sealed class McpNativeToolProvider(IHttpClientFactory httpClientFactory, ILogger<McpNativeToolProvider> logger)
{
    private readonly ConcurrentDictionary<string, IReadOnlyList<AITool>> _tools = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, IMcpClient> _clients = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<AITool> GetMakerTools(string subjectAgent)
    {
        if (_tools.TryGetValue(subjectAgent, out var cached))
            return cached;

        var server = subjectAgent switch
        {
            "filesystem-agent" => "mcp-filesystem",
            "terminal-agent" => "mcp-terminal",
            "playwright-agent" => "mcp-playwright",
            _ => null
        };

        if (server is null)
            return [];

        var client = GetOrCreateClient(server);
        var tools = client.ListToolsAsync().GetAwaiter().GetResult().Cast<AITool>().ToArray();
        _tools[subjectAgent] = tools;
        logger.LogInformation("[MCP-NATIVE] {SubjectAgent}: discovered {ToolCount} tools from {Server}", subjectAgent, tools.Length, server);
        return tools;
    }

    public IReadOnlyList<(string Name, string Description)> GetCatalog(string subjectAgent) =>
        GetMakerTools(subjectAgent).Select(t => (t.Name, t.Description)).ToArray();

    private IMcpClient GetOrCreateClient(string serverName)
    {
        if (_clients.TryGetValue(serverName, out var existing))
            return existing;

        var httpClient = httpClientFactory.CreateClient(serverName);
        var baseAddress = httpClient.BaseAddress
            ?? throw new InvalidOperationException($"MCP client '{serverName}' has no BaseAddress.");

        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(baseAddress, "/mcp"),
            TransportMode = HttpTransportMode.StreamableHttp,
            ConnectionTimeout = TimeSpan.FromSeconds(30),
        }, httpClient);

        var created = McpClient.CreateAsync(transport).GetAwaiter().GetResult();
        if (_clients.TryAdd(serverName, created))
            return created;

        created.DisposeAsync().AsTask().GetAwaiter().GetResult();
        return _clients[serverName];
    }
}
