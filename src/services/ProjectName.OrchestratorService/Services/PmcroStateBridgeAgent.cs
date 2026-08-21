// Services/PmcroStateBridgeAgent.cs
// ARCH-AGUI-STATE-001 (2026-07-13): wraps the keyed "Orchestrator" AIAgent so a
// running PMCR-O cycle's phase transitions reach the AG-UI frontend as
// STATE_SNAPSHOT events, not just the final tool_call_result once
// run_pmcro_cycle returns. Without this, useAgent/v2's agent.state never
// populates mid-cycle (confirmed by reading Program.cs/PmcroLoop.cs directly:
// the Orchestrator's sole tool is a single opaque AIFunction, and PmcroLoop
// only ever wrote frames to ITrailWriter, never to any AG-UI channel).
//
// Pattern follows Microsoft's own DelegatingAIAgent + DataContent example
// (learn.microsoft.com/en-us/agent-framework/integrations/ag-ui/state-management,
// mirrored in .agents/skills/copilotkit-ms-agent-framework-dotnet/references/
// state-rendering.md) -- adapted from that doc's two-phase client-state-sync
// shape down to what this Colony actually needs: a one-directional server ->
// client push of phase snapshots, not bidirectional client-editable state.
//
// BUILD RISK FLAG (unverified against the repo's actual pinned package,
// Microsoft.Agents.AI 1.13.0 stable): the signature below (`AgentSession?
// session`, `protected override`) matches Microsoft's current published docs
// (dated 2026-04-01, MAF 1.0 GA train), but this repo's other Microsoft.Agents.AI.*
// packages are pinned to 1.13.0-preview builds from 260703 and the AG-UI hosting
// package itself carries an existing "net11.0 compat UNVERIFIED" flag in
// Directory.Packages.props. If `dotnet build` reports CS0115 (no suitable method
// to override) or a missing `AgentSession` type on this line, the installed
// 1.13.0 surface likely still calls the parameter `AgentThread? thread` instead
// -- rename both the parameter type and the pass-through calls below to match
// whatever member IntelliSense/the compiler error actually reports, everything
// else in this file is unaffected by that rename.

using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace ProjectName.OrchestratorService.Services;

public sealed class PmcroStateBridgeAgent : DelegatingAIAgent
{
    private static readonly JsonSerializerOptions StateJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public PmcroStateBridgeAgent(AIAgent innerAgent) : base(innerAgent)
    {
    }

    protected override Task<AgentResponse> RunCoreAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return this.RunStreamingAsync(messages, session, options, cancellationToken)
            .ToAgentResponseAsync(cancellationToken);
    }

    protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var merged = Channel.CreateUnbounded<AgentResponseUpdate>();
        var stateChannel = Channel.CreateUnbounded<PmcroCycleStateSnapshot>();

        // Bind BEFORE starting the inner-run pump below -- Task.Run captures the
        // ExecutionContext (and therefore this AsyncLocal binding) at the point
        // it's scheduled, so PmcroLoop.RunAsync (invoked deep inside the inner
        // agent's run_pmcro_cycle tool call) sees this same writer.
        using var binding = PmcroStateBroadcast.Bind(stateChannel.Writer);

        // BUG-PARENTMSGID-001 (2026-07-13, agent-framework#3433): Ollama's
        // ChatResponseUpdate never sets MessageId. ChatClientAgent wraps it in
        // AgentResponseUpdate but AGUI's AsChatResponseUpdate() prefers
        // RawRepresentation when present, so the null MessageId flows straight
        // through to the AGUI serializer as messageId/parentMessageId: null.
        // @ag-ui/core's Zod schema is .optional() (undefined-only, not
        // .nullable()), so the frontend rejects the event: "Expected string,
        // received null". One synthesized id per streaming run matches the
        // upstream-documented workaround -- every update in a single
        // RunStreamingAsync call belongs to the same logical message/run.
        var syntheticMessageId = $"msg_{Guid.NewGuid():N}";

        var innerPump = Task.Run(async () =>
        {
            try
            {
                await foreach (var update in this.InnerAgent
                    .RunStreamingAsync(messages, session, options, cancellationToken)
                    .ConfigureAwait(false))
                {
                    if (string.IsNullOrEmpty(update.MessageId))
                    {
                        update.MessageId = syntheticMessageId;
                    }
                    if (update.RawRepresentation is ChatResponseUpdate chatUpdate &&
                        string.IsNullOrEmpty(chatUpdate.MessageId))
                    {
                        chatUpdate.MessageId = syntheticMessageId;
                    }

                    await merged.Writer.WriteAsync(update, cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                // The real work is done (or failed) -- stop accepting new phase
                // snapshots so the state pump below can drain and finish too.
                stateChannel.Writer.TryComplete();
            }
        }, cancellationToken);

        var statePump = Task.Run(async () =>
        {
            await foreach (var snapshot in stateChannel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                await merged.Writer.WriteAsync(ToStateUpdate(snapshot), cancellationToken).ConfigureAwait(false);
            }
        }, cancellationToken);

        _ = Task.WhenAll(innerPump, statePump).ContinueWith(
            t => merged.Writer.TryComplete(t.Exception),
            TaskScheduler.Default);

        await foreach (var item in merged.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return item;
        }
    }

    private static AgentResponseUpdate ToStateUpdate(PmcroCycleStateSnapshot snapshot)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(snapshot, StateJsonOptions);
        return new AgentResponseUpdate
        {
            Contents = [new DataContent(bytes, "application/json")]
        };
    }
}
