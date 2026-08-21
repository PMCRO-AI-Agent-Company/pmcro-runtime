// Services/SubjectAgentRegistry.cs
// Maps subjectAgent name strings to registered MAF AIAgent instances.
//
// Pattern: Anthropic Orchestrator-Workers
// The Orchestrator resolves the correct subject AIAgent by name at cycle
// dispatch time. Each registered agent is a true MAF AIAgent with its own
// tool set, name, and identity — visible to DevUI and the WorkflowBuilder.
//
// To add a new subject agent (e.g. terminal-agent, playwright-agent):
//   1. Register it as a keyed AIAgent in Program.cs
//   2. Add it to SubjectAgentRegistry via Register() at startup
//   3. Wire its tools in McpToolCache.GetMakerTools()

using Microsoft.Agents.AI;

namespace ProjectName.OrchestratorService.Services;

public interface ISubjectAgentRegistry
{
    /// <summary>
    /// Registers a subject AIAgent under the given name.
    /// Called once at startup for each colony subject agent.
    /// </summary>
    void Register(string name, AIAgent agent);

    /// <summary>
    /// Resolves a subject AIAgent by name.
    /// Returns null if no agent is registered under that name.
    /// </summary>
    AIAgent? Resolve(string name);

    /// <summary>
    /// Returns all registered subject agent names.
    /// </summary>
    IReadOnlyList<string> ListAgents();
}

public sealed class SubjectAgentRegistry : ISubjectAgentRegistry
{
    private readonly Dictionary<string, AIAgent> _agents =
        new(StringComparer.OrdinalIgnoreCase);

    public void Register(string name, AIAgent agent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(agent);
        _agents[name] = agent;
    }

    public AIAgent? Resolve(string name) =>
        _agents.TryGetValue(name, out var a) ? a : null;

    public IReadOnlyList<string> ListAgents() =>
        _agents.Keys.ToList().AsReadOnly();
}
