// Workflows/Declarative/SubjectAgentRegistryProvider.cs
// ARCH-DECLARATIVE-001: bridges declarative-workflow agent invocation to the
// Colony's existing local ISubjectAgentRegistry (qwen3:8b via Ollama), instead
// of Foundry cloud (AzureAgentProvider). agent.name in YAML == a name
// registered via ISubjectAgentRegistry.Register() in Program.cs.
//
// Conversation state: kept in-memory per conversationId. This is a first cut --
// good enough for InProcessExecution + CheckpointManager.CreateInMemory(); a
// durable store is a follow-up once the YAML shape itself is validated end to
// end (see pattern-a-macro-cycle.yaml in this folder).
using System.Collections.Concurrent;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows.Declarative;
using Microsoft.Extensions.AI;
using ProjectName.OrchestratorService.Services;

namespace ProjectName.OrchestratorService.Workflows.Declarative;

public sealed class SubjectAgentRegistryProvider(ISubjectAgentRegistry registry) : ResponseAgentProvider
{
    // conversationId -> message history. AgentThread would be the "correct"
    // MAF-native home for this, but the four ResponseAgentProvider methods
    // below take/return raw ChatMessage, not AgentThread, so we own the store.
    private readonly ConcurrentDictionary<string, List<ChatMessage>> _conversations = new();

    public override Task<string> CreateConversationAsync(CancellationToken ct)
    {
        var id = Guid.NewGuid().ToString("N");
        _conversations[id] = [];
        return Task.FromResult(id);
    }

    public override Task<ChatMessage> CreateMessageAsync(string conversationId, ChatMessage message, CancellationToken ct)
    {
        var list = _conversations.GetOrAdd(conversationId, _ => []);
        list.Add(message);
        return Task.FromResult(message);
    }

    public override Task<ChatMessage> GetMessageAsync(string conversationId, string messageId, CancellationToken ct)
    {
        // Colony messages don't carry a stable messageId today; ChatMessage.MessageId
        // (Microsoft.Extensions.AI) is the natural key once producers start setting it.
        var msg = _conversations.TryGetValue(conversationId, out var list)
            ? list.FirstOrDefault(m => m.MessageId == messageId)
            : null;
        return Task.FromResult(msg ?? throw new KeyNotFoundException(
            $"No message '{messageId}' in conversation '{conversationId}'."));
    }

    public override async IAsyncEnumerable<ChatMessage> GetMessagesAsync(
        string conversationId, int? limit, string? after, string? before, bool newestFirst,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        if (!_conversations.TryGetValue(conversationId, out var list)) yield break;
        IEnumerable<ChatMessage> ordered = newestFirst ? Enumerable.Reverse(list) : list;
        if (limit is int n) ordered = ordered.Take(n);
        foreach (var m in ordered)
        {
            ct.ThrowIfCancellationRequested();
            yield return m;
            await Task.Yield();
        }
    }

    public override async IAsyncEnumerable<AgentResponseUpdate> InvokeAgentAsync(
        string agentName, string? conversationId, string? instructions,
        IEnumerable<ChatMessage>? messages, IDictionary<string, object?>? arguments,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var agent = registry.Resolve(agentName)
            ?? throw new InvalidOperationException(
                $"No AIAgent registered under name '{agentName}'. Register it in Program.cs via ISubjectAgentRegistry.");

        var input = messages?.ToList() ?? [];
        if (conversationId is not null)
        {
            var history = _conversations.GetOrAdd(conversationId, _ => []);
            history.AddRange(input);
        }

        await foreach (var update in agent.RunStreamingAsync(input, cancellationToken: ct))
        {
            if (conversationId is not null && update.Contents.Count > 0)
                _conversations[conversationId].Add(new ChatMessage(update.Role ?? ChatRole.Assistant, update.Contents));
            yield return update;
        }
    }
}
