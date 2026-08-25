using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProjectName.OrchestratorService.Configuration;

namespace ProjectName.OrchestratorService.Skills;

/// <summary>
/// Keeps the generated MAF skill staging directory synchronized with the
/// configured external Agent Skills sources. The source repositories remain
/// authoritative; staging is only a runtime cache.
/// </summary>
public sealed class MarketplaceSkillsWatcherService(
    ILogger<MarketplaceSkillsWatcherService> logger,
    MarketplaceSkillsMaterializer materializer,
    IOptions<OrchestratorConfig> config) : IHostedService, IDisposable
{
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Timer? _debounce;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await materializer.MaterializeAsync(cancellationToken);

        foreach (var source in ResolveSkillSources())
        {
            try
            {
                var watcher = new FileSystemWatcher(source)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.LastWrite |
                                   NotifyFilters.CreationTime |
                                   NotifyFilters.Size |
                                   NotifyFilters.FileName |
                                   NotifyFilters.DirectoryName,
                    EnableRaisingEvents = true,
                };

                watcher.Changed += (_, _) => ScheduleRefresh();
                watcher.Created += (_, _) => ScheduleRefresh();
                watcher.Deleted += (_, _) => ScheduleRefresh();
                watcher.Renamed += (_, _) => ScheduleRefresh();
                _watchers.Add(watcher);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "[MarketplaceSkills] unable to watch skill source {Source}; continuing without hot reload for it.",
                    source);
            }
        }

        logger.LogInformation(
            "[MarketplaceSkills] watching {Count} configured Agent Skills source(s).",
            _watchers.Count);
    }

    private IEnumerable<string> ResolveSkillSources()
    {
        foreach (var configuredPath in config.Value.SkillPaths ?? [])
        {
            if (string.IsNullOrWhiteSpace(configuredPath))
                continue;

            var source = Path.IsPathRooted(configuredPath)
                ? Path.GetFullPath(configuredPath)
                : Path.GetFullPath(Path.Combine(
                    config.Value.FileSystemRoot,
                    configuredPath.Replace('/', Path.DirectorySeparatorChar)));

            if (Directory.Exists(source))
                yield return source;
            else
                logger.LogWarning("[MarketplaceSkills] configured skill source does not exist: {Source}", source);
        }
    }

    private void ScheduleRefresh()
    {
        _debounce?.Dispose();
        _debounce = new Timer(async _ =>
        {
            if (!await _gate.WaitAsync(0))
                return;

            try
            {
                await materializer.MaterializeAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[MarketplaceSkills] skill re-materialization failed.");
            }
            finally
            {
                _gate.Release();
            }
        }, null, 500, Timeout.Infinite);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        foreach (var watcher in _watchers)
            watcher.Dispose();

        _watchers.Clear();
        _debounce?.Dispose();
        _gate.Dispose();
    }
}
