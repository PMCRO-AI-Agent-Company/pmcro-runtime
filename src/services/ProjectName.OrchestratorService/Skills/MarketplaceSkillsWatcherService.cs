// src/services/ProjectName.OrchestratorService/Skills/MarketplaceSkillsWatcherService.cs
using System.Text.Json;
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
///
/// ARCH-MARKETPLACE-BRIDGE-002 (2026-08-22): the original version of this class
/// watched ONLY marketplace.json. That left a real gap, confirmed empirically
/// the same day: editing a SKILL.md directly under a staged plugin's source repo
/// (e.g. Z:\pmcro-skills\plugins\pmcro-orchestrator\skills\orchestrate\SKILL.md)
/// produced no re-materialization -- the running app kept serving the stale copy
/// from StagingRoot until marketplace.json itself was touched or the app
/// restarted. Since every staged plugin's "source" already points cross-repo
/// (../pmcro-skills/plugins/..., ../dotnet-skills/plugins/..., etc -- see
/// marketplace.json), this class now also watches each staged plugin's source
/// root recursively, so editing a skill's own files hot-reloads the same way
/// editing marketplace.json does. The source-watcher list is rebuilt every time
/// marketplace.json changes too, so adding/removing/re-staging a plugin updates
/// what's watched without a restart.
/// </summary>
public sealed class MarketplaceSkillsWatcherService(
    ILogger<MarketplaceSkillsWatcherService> logger,
    MarketplaceSkillsMaterializer materializer,
    IOptions<OrchestratorConfig> config) : IHostedService, IDisposable
{
    private FileSystemWatcher? _watcher;
    private readonly List<FileSystemWatcher> _sourceWatchers = [];
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

        RefreshSourceWatchers();
    }

    // ARCH-MARKETPLACE-BRIDGE-002: rebuilds the per-plugin source watchers from
    // the current marketplace.json contents. Called once at startup and again
    // on every debounced marketplace.json change, so adding/removing/re-staging
    // a plugin updates what's watched without an app restart. Disposes the old
    // watcher set first -- this is the only place _sourceWatchers is mutated,
    // and it always runs inside the same _gate-guarded path as MaterializeAsync
    // (see ScheduleRefresh), so there is no concurrent-mutation race with itself.
    private void RefreshSourceWatchers()
    {
        foreach (var w in _sourceWatchers) w.Dispose();
        _sourceWatchers.Clear();

        foreach (var pluginRoot in GetStagedPluginSourceDirs())
        {
            try
            {
                var watcher = new FileSystemWatcher(pluginRoot)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime
                        | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.DirectoryName,
                    EnableRaisingEvents = true,
                };
                watcher.Changed += (_, _) => ScheduleRefresh();
                watcher.Created += (_, _) => ScheduleRefresh();
                watcher.Deleted += (_, _) => ScheduleRefresh();
                watcher.Renamed += (_, _) => ScheduleRefresh();
                _sourceWatchers.Add(watcher);
            }
            catch (Exception ex)
            {
                // A single bad plugin root (permissions, drive unmounted, etc.)
                // should not prevent watching the rest.
                logger.LogWarning(ex,
                    "[MarketplaceSkills] failed to start source watcher for {PluginRoot} -- skipping.",
                    pluginRoot);
            }
        }

        logger.LogInformation(
            "[MarketplaceSkills] Watching {Count} staged plugin source director(ies) for hot-reload.",
            _sourceWatchers.Count);
    }

    // Reads marketplace.json directly (mirrors MarketplaceSkillsMaterializer's own
    // parsing -- deliberately not shared/refactored into that class, since this
    // read is watcher-setup bookkeeping, not materialization, and keeping them
    // separate avoids coupling watcher lifecycle to materializer internals).
    // Only "stage": true plugins are watched, matching what MaterializeAsync
    // actually copies -- watching stage:false entries would just burn file
    // handles on directories nothing ever reads from StagingRoot.
    private List<string> GetStagedPluginSourceDirs()
    {
        var result = new List<string>();
        var repoRoot = config.Value.FileSystemRoot;
        var marketplacePath = Path.Combine(repoRoot,
            config.Value.MarketplaceRelativePath.Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(marketplacePath)) return result;

        try
        {
            var text = File.ReadAllText(marketplacePath);
            using var doc = JsonDocument.Parse(text);
            if (!doc.RootElement.TryGetProperty("plugins", out var plugins) || plugins.ValueKind != JsonValueKind.Array)
                return result;

            foreach (var plugin in plugins.EnumerateArray())
            {
                var source = plugin.TryGetProperty("source", out var s) ? s.GetString() : null;
                if (string.IsNullOrWhiteSpace(source)) continue;

                var stage = !plugin.TryGetProperty("stage", out var stageProp)
                    || stageProp.ValueKind != JsonValueKind.False;
                if (!stage) continue;

                var pluginRoot = Path.GetFullPath(Path.Combine(repoRoot, source));
                if (Directory.Exists(pluginRoot))
                    result.Add(pluginRoot);
                else
                    logger.LogWarning(
                        "[MarketplaceSkills] staged plugin source not found, cannot watch: {PluginRoot}",
                        pluginRoot);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "[MarketplaceSkills] failed to parse {Path} while building source-watcher list.",
                marketplacePath);
        }

        return result;
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
        foreach (var w in _sourceWatchers) w.Dispose();
        _sourceWatchers.Clear();
        _debounce?.Dispose();
        _gate.Dispose();
    }
}
