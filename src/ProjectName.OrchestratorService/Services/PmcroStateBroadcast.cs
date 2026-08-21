// Services/PmcroStateBroadcast.cs
// Lightweight in-process broadcast for cycle phase snapshots (AG-UI / DevUI).

namespace ProjectName.OrchestratorService.Services;

public sealed record PmcroCycleStateSnapshot(
    string TrailId,
    int Cycle,
    string Phase,
    string? Disposition = null);

public static class PmcroStateBroadcast
{
    public static event Action<PmcroCycleStateSnapshot>? OnSnapshot;

    public static void Publish(PmcroCycleStateSnapshot snapshot)
    {
        try { OnSnapshot?.Invoke(snapshot); }
        catch { /* never fail the loop on UI broadcast */ }
    }
}
