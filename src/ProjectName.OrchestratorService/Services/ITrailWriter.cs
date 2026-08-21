// Services/ITrailWriter.cs
// PMCR-O trail persistence contract.

using ProjectName.OrchestratorService.Loop;

namespace ProjectName.OrchestratorService.Services;

public interface ITrailWriter
{
    /// <summary>Writes a full cycle's frames to the trail.</summary>
    Task WriteAsync(
        string subjectAgent,
        string trailId,
        string seedIntent,
        int cycle,
        PlannerFrame planner,
        MakerFrame maker,
        CheckerFrame checker,
        ReflectorFrame reflector,
        CancellationToken ct = default);

    /// <summary>Seals the trail with the final result.</summary>
    Task SealAsync(
        string subjectAgent,
        string trailId,
        PmcroResult result,
        CancellationToken ct = default);
}
