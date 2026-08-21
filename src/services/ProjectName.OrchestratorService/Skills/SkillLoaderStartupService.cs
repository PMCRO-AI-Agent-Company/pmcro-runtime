using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace ProjectName.OrchestratorService.Skills;

// ARCH-SKILLLOADER-DISK-001 (2026-07-20): now depends on MarketplaceSkillsMaterializer
// so LoadAll() reads from the same on-disk StagingRoot the materializer populates,
// instead of the disconnected embedded-resource scan it used to run. Program.cs
// already awaits materializer.MaterializeAsync() synchronously right after
// app.Build(), before hosted services (including this one) start -- so StagingRoot
// is guaranteed populated by the time StartAsync below runs.
public sealed class SkillLoaderStartupService(
    PmcroSkillLoader loader,
    MarketplaceSkillsMaterializer materializer) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        loader.LoadAll(materializer.StagingRoot);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
