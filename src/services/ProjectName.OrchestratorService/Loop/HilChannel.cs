// Loop/HilChannel.cs
// HIL (Human-in-the-Loop) gate — surfaces TYPE1 approval requests in the DevUI
// conversation stream and blocks execution until the human responds.
//
// Dev mode (DevUiHilChannel): emits a chat message asking for approval,
// then polls a concurrent dictionary for a response keyed by request ID.
// The DevUI /hil/approve and /hil/deny endpoints write into that dictionary.
//
// Production: replace with a durable approval channel (e.g. Azure Service Bus,
// SignalR hub, or a dedicated approval workflow service).

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace ProjectName.OrchestratorService.Loop;

// ── Abstraction ───────────────────────────────────────────────────────────────

public interface IHilChannel
{
    /// <summary>
    /// Requests human approval for a TYPE1 action.
    /// Blocks until approved, denied, or timeout expires.
    /// Returns true = approved, false = denied.
    /// </summary>
    Task<bool> RequestAsync(
        string requestId,
        string action,
        string target,
        string trailId,
        CancellationToken ct = default);

    /// <summary>
    /// Called by the approval endpoint to resolve a pending request.
    /// </summary>
    void Resolve(string requestId, bool approved);
}

// ── Dev implementation ────────────────────────────────────────────────────────

public sealed class DevUiHilChannel(ILogger<DevUiHilChannel> logger) : IHilChannel
{
    // Keyed by requestId → TaskCompletionSource<bool>
    private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _pending = new();

    // Timeout before auto-deny (prevents indefinite blocking in dev)
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);

    // DEV-GODMODE-001 (DISABLED 2026-07-13): this override used to instantly approve
    // every TYPE1 request, bypassing MAAI-001/EC-002's human approval gate entirely —
    // a direct violation of .clinerules/pmcro-loop.md §4 ("Auto-Approve Ban: forbidden
    // from enabling auto-approval for any TYPE1 tool category"). Confirmed with the
    // human operator (2026-07-13, Night Shift / Succession Law wiring session) that
    // full HIL gating stays on even for the autonomous multi-cycle loop — automation
    // is the trail-to-trail hand-off (NextSeedIntent), never the TYPE1 approval itself.
    // Left as a named constant (not deleted) so the historical violation stays visible
    // in the diff rather than silently disappearing.
    private const bool DevAutoApprove = false;

    public Task<bool> RequestAsync(
        string requestId,
        string action,
        string target,
        string trailId,
        CancellationToken ct = default)
    {
        if (DevAutoApprove)
        {
#pragma warning disable CS0162 // Intentionally unreachable: DevAutoApprove is a
            // hardcoded `false` const so this historical bypass branch (DEV-GODMODE-001,
            // disabled 2026-07-13) never executes. Kept in the diff on purpose — see the
            // comment above DevAutoApprove for why it isn't simply deleted.
            logger.LogWarning(
                "[HIL] 🤖 AUTO-APPROVED (DEV-GODMODE-001, HIL gate bypassed) — id={Id} action={Action} target={Target} trail={Trail}",
                requestId, action, target, trailId);
            return Task.FromResult(true);
#pragma warning restore CS0162
        }

        return RequestAsyncCore(requestId, action, target, trailId, ct);
    }

    private async Task<bool> RequestAsyncCore(
        string requestId,
        string action,
        string target,
        string trailId,
        CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[requestId] = tcs;

        logger.LogWarning(
            "[HIL] ⏸ TYPE1 approval required — id={Id} action={Action} target={Target} trail={Trail}\n" +
            "      Approve: POST /hil/approve?id={Id}\n" +
            "      Deny:    POST /hil/deny?id={Id}",
            requestId, action, target, trailId, requestId, requestId);

        using var timeoutCts = new CancellationTokenSource(Timeout);
        using var linked     = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        try
        {
            await using var reg = linked.Token.Register(() => tcs.TrySetResult(false));
            var approved = await tcs.Task;

            logger.LogInformation(
                "[HIL] {Result} — id={Id} action={Action}",
                approved ? "✅ APPROVED" : "❌ DENIED", requestId, action);

            return approved;
        }
        finally
        {
            _pending.TryRemove(requestId, out _);
        }
    }

    public void Resolve(string requestId, bool approved)
    {
        if (_pending.TryGetValue(requestId, out var tcs))
            tcs.TrySetResult(approved);
        else
            logger.LogWarning("[HIL] Resolve called for unknown requestId={Id}", requestId);
    }
}
