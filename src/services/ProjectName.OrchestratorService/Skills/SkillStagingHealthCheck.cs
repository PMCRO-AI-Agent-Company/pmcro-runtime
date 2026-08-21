using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ProjectName.OrchestratorService.Skills;

/// <summary>
/// Readiness check for the native MAF skill source. Liveness remains owned by
/// ServiceDefaults; readiness must fail when the staging tree is unavailable.
/// </summary>
public sealed class SkillStagingHealthCheck(MarketplaceSkillsMaterializer materializer) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Directory.Exists(materializer.StagingRoot))
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                $"MAF skill staging directory is missing: {materializer.StagingRoot}"));
        }

        var skillCount = Directory.EnumerateFiles(
            materializer.StagingRoot,
            "SKILL.md",
            SearchOption.AllDirectories).Count();
        return skillCount > 0
            ? Task.FromResult(HealthCheckResult.Healthy($"{skillCount} skill manifests staged."))
            : Task.FromResult(HealthCheckResult.Unhealthy("No SKILL.md manifests are staged."));
    }
}
