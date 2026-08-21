// Services/PmcroStateBroadcast.cs
// ARCH-AGUI-STATE-001 (2026-07-13): ambient per-run state broadcast so PmcroLoop
// (which knows nothing about AG-UI) can publish per-phase snapshots that
// PmcroStateBridgeAgent (which knows nothing about PmcroLoop's internals) turns
// into AG-UI STATE_SNAPSHOT events (see Microsoft's DelegatingAIAgent + DataContent
// pattern in .agents/skills/copilotkit-ms-agent-framework-dotnet/references/
// state-rendering.md).
//
// Mechanism: PmcroStateBridgeAgent.RunCoreStreamingAsync binds an AsyncLocal
// ChannelWriter at the top of one AG-UI run, BEFORE it starts the inner
// Orchestrator agent running (which is what eventually invokes run_pmcro_cycle
// -> PmcroLoop.RunAsync). Because AsyncLocal flows through the same logical
// async call chain (awaits, Task.Run without ExecutionContext.SuppressFlow),
// PmcroLoop.RunAsync sees the same ambient writer without any trailId plumbing
// between the AG-UI layer and the loop layer.
//
// CAVEAT: if PmcroLoop.RunAsync is ever invoked OUTSIDE of a bound
// PmcroStateBridgeAgent run (e.g. a future test harness, a direct DI-resolved
// call, a background job), Publish() is a silent no-op (Value is null) --
// deliberately, so PmcroLoop never has a hard dependency on AG-UI being present.

using System.Threading.Channels;

namespace ProjectName.OrchestratorService.Services;

public static class PmcroStateBroadcast
{
    private static readonly AsyncLocal<ChannelWriter<PmcroCycleStateSnapshot>?> _writer = new();

    /// <summary>
    /// Binds the ambient writer for the current async flow. Dispose the returned
    /// handle to unbind (defensive -- AsyncLocal already scopes to this flow and
    /// its children, but explicit unbinding avoids a stale writer surviving into
    /// unrelated work if this flow's Task is ever awaited from elsewhere).
    /// </summary>
    public static IDisposable Bind(ChannelWriter<PmcroCycleStateSnapshot> writer)
    {
        _writer.Value = writer;
        return new Unbinder();
    }

    /// <summary>
    /// Publish a phase snapshot. No-op if nothing is bound (see CAVEAT above) --
    /// intentionally fire-and-forget (TryWrite on an unbounded channel never
    /// blocks and never throws), so PmcroLoop's own cycle logic is never
    /// affected by whether anyone is listening.
    /// </summary>
    public static void Publish(PmcroCycleStateSnapshot snapshot) => _writer.Value?.TryWrite(snapshot);

    private sealed class Unbinder : IDisposable
    {
        public void Dispose() => _writer.Value = null;
    }
}

// UI-relevant fields only, per state-rendering.md guidance -- this is the
// contract between backend phase transitions and frontend rendering, not a
// dump of PmcroLoop's full internal frame state.
public sealed record PmcroCycleStateSnapshot(
    string TrailId,
    int Cycle,
    string Phase,           // "Planning" | "Making" | "Checking" | "Reflecting" | "CycleComplete" | "Sealed" | "Error"
    string? LastAction = null,
    string? Disposition = null,   // "Accept" | "Retry" | "Halt" -- only set once known
    bool? AllPassed = null);
