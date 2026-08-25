// Loop/HilChannel.cs
// HIL (Human-in-the-Loop) gate — surfaces TYPE1 approval requests and blocks execution until the human responds.

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace ProjectName.OrchestratorService.Loop;

public interface IHilChannel
{
    Task<bool> RequestAsync(string requestId, string action, string target, string trailId, CancellationToken ct = default);
    void Resolve(string requestId, bool approved);

    /// <summary>Resolves the most recently opened pending approval request.</summary>
    bool ResolveLatest(bool approved);
}

public sealed class DevUiHilChannel(ILogger<DevUiHilChannel> logger) : IHilChannel
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _pending = new();
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);
    private string? _latestPendingRequestId;
    private readonly object _latestGate = new();

    // TYPE1 auto-approval is forbidden.
    private const bool DevAutoApprove = false;

    public Task<bool> RequestAsync(string requestId, string action, string target, string trailId, CancellationToken ct = default)
    {
        if (DevAutoApprove)
            throw new InvalidOperationException("TYPE1 auto-approval is forbidden.");
        return RequestAsyncCore(requestId, action, target, trailId, ct);
    }

    private async Task<bool> RequestAsyncCore(string requestId, string action, string target, string trailId, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[requestId] = tcs;
        lock (_latestGate) _latestPendingRequestId = requestId;

        logger.LogWarning(
            "[HIL] TYPE1 approval required — id={Id} action={Action} target={Target} trail={Trail}\n" +
            "      Approve: POST /hil/approve?id={Id}\n" +
            "      Orchestrator command: /pmcro-orchestrator:approve",
            requestId, action, target, trailId, requestId);

        using var timeoutCts = new CancellationTokenSource(Timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        try
        {
            using var reg = linked.Token.Register(() => tcs.TrySetResult(false));
            var approved = await tcs.Task;
            logger.LogInformation("[HIL] {Result} — id={Id} action={Action}", approved ? "APPROVED" : "DENIED", requestId, action);
            return approved;
        }
        finally
        {
            _pending.TryRemove(requestId, out _);
            lock (_latestGate)
            {
                if (_latestPendingRequestId == requestId)
                    _latestPendingRequestId = _pending.Keys.LastOrDefault();
            }
        }
    }

    public void Resolve(string requestId, bool approved)
    {
        if (_pending.TryGetValue(requestId, out var tcs))
            tcs.TrySetResult(approved);
        else
            logger.LogWarning("[HIL] Resolve called for unknown requestId={Id}", requestId);
    }

    public bool ResolveLatest(bool approved)
    {
        string? requestId;
        lock (_latestGate) requestId = _latestPendingRequestId;
        return requestId is not null && _pending.TryGetValue(requestId, out var tcs) && tcs.TrySetResult(approved);
    }
}
