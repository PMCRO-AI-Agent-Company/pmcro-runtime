// Loop/HilChannel.cs
// HIL gate for TYPE1 dispatch. Dev mode polls a concurrent dictionary.

using System.Collections.Concurrent;

namespace ProjectName.OrchestratorService.Loop;

public interface IHilChannel
{
    Task<bool> RequestAsync(string requestId, string action, string target, string trailId, CancellationToken ct = default);
    void Resolve(string requestId, bool approved);
}

public sealed class DevUiHilChannel : IHilChannel
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _pending = new();
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);

    public Task<bool> RequestAsync(string requestId, string action, string target, string trailId, CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[requestId] = tcs;
        using var timeoutCts = new CancellationTokenSource(Timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        linked.Token.Register(() => tcs.TrySetResult(false));
        return tcs.Task;
    }

    public void Resolve(string requestId, bool approved)
    {
        if (_pending.TryGetValue(requestId, out var tcs))
            tcs.TrySetResult(approved);
    }
}
