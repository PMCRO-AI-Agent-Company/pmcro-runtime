// src/services/ProjectName.OrchestratorService/Skills/MarketplaceSkillsWatcherService.cs
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProjectName.OrchestratorService.Configuration;

namespace ProjectName.OrchestratorService.Skills;

/// <summary>
/// ARCH-MARKETPLACE-BRIDGE-001: runs MarketplaceSkillsMaterializer once at
/// startup and then watches .agents/plugins/marketplace.json for changes,
/// re-materializing (debounced) on each save. This is "hot-loading when a new
/// marketplace package is installed" in practice: drop a new plugin under
/// catalog/, add its entry to marketplace.json, save -- the next debounced pass
/// copies it into StagingRoot without an app restart.
///
/// NOTE: this hosted service's own StartAsync does NOT run early enough to cover
/// the FIRST materialization -- IHostedService.StartAsync fires when app.RunAsync()
/// starts the host, which is after Program.cs's synchronous
/// GetRequiredKeyedService&lt;AIAgent&gt;("Orchestrator"/"HarnessAgent") calls that
/// construct AgentSkillsProvider. Program.cs therefore also calls
/// MaterializeAsync() once, synchronously, right after app.Build() -- see the
/// ARCH-MARKETPLACE-BRIDGE-001 comment there. This service's StartAsync call is
/// a harmless, idempotent second pass; its real job is the watcher for everything
/// after startup.
///
/// BUILD-RISK FLAG (unverified, same spirit as this file's ARCH-HARNESS-001 in
/// Program.cs): whether MAF's AgentSkillsProvider re-scans StagingRoot on every
/// advertise()/load_skill() call, or only once at construction, is not confirmed
/// against this repo's pinned Microsoft.Agents.AI version from inside this
/// sandbox (no network access to browse the package source from here).
/// Re-materializing the files on disk is correct regardless of which is true; if
/// skills still don't hot-load end-to-end after this change, the remaining gap is
/// AgentSkillsProvider's own caching behavior, not this class -- next step would
/// be confirming that against Microsoft's docs/source directly.
/// </summary>
public sealed class MarketplaceSkillsWatcherService(
    ILogger<MarketplaceSkillsWatcherService> logger,
    MarketplaceSkillsMaterializer materializer,
    IOptions<OrchestratorConfig> config) : IHostedService, IDisposable
{
    private FileSystemWatcher? _watcher;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Timer? _debounce;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await materializer.MaterializeAsync(cancellationToken);
        // ARCH-NATIVE-MAF-001: no PmcroSkillLoader refresh needed here anymore --
        // SkillManifestReader reads Colony Laws directly from marketplace source
        // paths on each call (no cached registry to keep in sync), and MAF's
        // AgentSkillsProvider reads StagingRoot natively for everything else.

        var marketplaceDir = Path.Combine(config.Value.FileSystemRoot, ".agents", "plugins");
        if (!Directory.Exists(marketplaceDir))
        {
            logger.LogWarning(
                "[MarketplaceSkills] {Dir} does not exist -- hot-reload watcher not started.",
                marketplaceDir);
            return;
        }

        _watcher = new FileSystemWatcher(marketplaceDir, "marketplace.json")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };
        _watcher.Changed += (_, _) => ScheduleRefresh();
        _watcher.Created += (_, _) => ScheduleRefresh();
    }

    // Debounced: editors/tools often fire several Changed events for one logical
    // save. Coalesce into a single re-materialization ~500ms after the last event.
    private void ScheduleRefresh()
    {
        _debounce?.Dispose();
        _debounce = new Timer(async _ =>
        {
            if (!await _gate.WaitAsync(0)) return;
            try
            {
                await materializer.MaterializeAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[MarketplaceSkills] re-materialization failed after marketplace.json change.");
            }
            finally { _gate.Release(); }
        }, null, 500, Timeout.Infinite);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _debounce?.Dispose();
        _gate.Dispose();
    }
}
