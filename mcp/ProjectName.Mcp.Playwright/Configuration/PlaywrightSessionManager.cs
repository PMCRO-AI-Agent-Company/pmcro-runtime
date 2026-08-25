// src/Mcps/ProjectName.Mcp.Playwright/Configuration/PlaywrightSessionManager.cs
// ═══════════════════════════════════════════════════════════════════════════════
// PROJECTNAME — MCP.PLAYWRIGHT
// File       : Configuration/PlaywrightSessionManager.cs
// Identity   : Browser Lifecycle Manager (PW-LAW-005 — Serial Execution Guard)
// Law Anchor : PW-LAW-005 (serial page execution), SAFETY-003, ARCH-013
// ───────────────────────────────────────────────────────────────────────────────
// Patchright is a drop-in replacement for Microsoft.Playwright — it ships the
// same public API under the Microsoft.Playwright namespace. The only difference
// is the underlying Chromium binary (patched for stealth). No namespace change
// required vs standard Playwright.
//
// One IPage at a time. Period. PW-LAW-005 is enforced via SemaphoreSlim(1,1).
// Callers must acquire the session lock before touching the page.
// The SessionManager owns launch/teardown — Tools never call IBrowser directly.
//
// Registered as a singleton (Program.cs). Disposable — Aspire will dispose it
// on shutdown, which closes the browser gracefully.
// ═══════════════════════════════════════════════════════════════════════════════

using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace ProjectName.Mcp.Playwright.Configuration;

public sealed class PlaywrightSessionManager(PlaywrightConfig config, ILogger<PlaywrightSessionManager> logger)
    : IAsyncDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IPlaywright? _playwright;
    private IBrowser?    _browser;
    private IPage?       _page;
    private DownloadRecord? _lastDownload;

    public bool IsLaunched => _browser is not null;

    /// <summary>
    /// EC-PW-002 fix: the most recent browser-triggered download captured on the
    /// active page, or null if none has occurred this session. Safe to call
    /// without the lock — reads a volatile ref, same pattern as GetStatus().
    /// </summary>
    public DownloadRecord? GetLastDownload() => _lastDownload;

    // ── Status snapshot (safe to call without the lock — reads volatile refs) ─
    public BrowserSessionStatus GetStatus() => new(
        IsLaunched:           IsLaunched,
        HasActivePage:        _page is not null,
        CurrentUrl:           _page?.Url,
        NavigationTimeoutMs:  config.NavigationTimeoutMs,
        ActionTimeoutMs:      config.ActionTimeoutMs,
        Headless:             config.Headless
    );

    /// <summary>
    /// Ensures Patchright-Chromium is launched and a page is ready.
    /// Idempotent — safe to call if already launched.
    /// Must be called while holding the session lock.
    /// </summary>
    public async Task EnsureLaunchedAsync()
    {
        if (IsLaunched) return;

        logger.LogInformation("[Playwright] Launching Patchright-Chromium (headless={Headless})", config.Headless);

        // Patchright exposes the same Microsoft.Playwright.Playwright.CreateAsync() entry point.
        _playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        _browser    = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = config.Headless
        });
        _page = await _browser.NewPageAsync();
        _page.SetDefaultNavigationTimeout(config.NavigationTimeoutMs);
        _page.SetDefaultTimeout(config.ActionTimeoutMs);

        // EC-PW-002 fix (2026-08-24): subscribe to the page's Download event so
        // browser-triggered downloads (e.g. a "Generate PDF" button that isn't a
        // navigation or an explicit screenshot call) are actually captured instead
        // of silently vanishing into the headless browser's ether. Mirrors the
        // ScreenshotDir sandbox pattern via ResolveDownloadPath (PW-LAW-007).
        _page.Download += async (_, download) =>
        {
            try
            {
                var path = config.ResolveDownloadPath(download.SuggestedFilename);
                await download.SaveAsAsync(path);
                var info = new FileInfo(path);
                _lastDownload = new DownloadRecord(
                    SuggestedFilename: download.SuggestedFilename,
                    SavedPath:         path,
                    Bytes:             info.Exists ? info.Length : 0,
                    Url:               download.Url,
                    CapturedAtUtc:     DateTime.UtcNow);
                logger.LogInformation(
                    "[Playwright] Download captured: suggested='{Suggested}' savedTo='{Path}' bytes={Bytes} url={Url}",
                    download.SuggestedFilename, path, _lastDownload.Bytes, download.Url);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[Playwright] Download event fired but capture failed: {Message}", ex.Message);
                _lastDownload = new DownloadRecord(
                    SuggestedFilename: download.SuggestedFilename,
                    SavedPath:         null,
                    Bytes:             0,
                    Url:               download.Url,
                    CapturedAtUtc:     DateTime.UtcNow,
                    Error:             ex.Message);
            }
        };

        logger.LogInformation("[Playwright] Browser launched, page ready");
    }

    /// <summary>
    /// Acquires the serial execution lock (PW-LAW-005).
    /// Returns an <see cref="IDisposable"/> that releases the lock on disposal.
    /// </summary>
    public async Task<IDisposable> AcquireLockAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        return new LockReleaser(_lock);
    }

    /// <summary>
    /// Returns the active page. Throws if not launched — callers must call
    /// EnsureLaunchedAsync first (while holding the lock).
    /// </summary>
    public IPage GetPage()
    {
        if (_page is null)
            throw new InvalidOperationException("Browser not launched. Call EnsureLaunchedAsync first.");
        return _page;
    }

    public async ValueTask DisposeAsync()
    {
        logger.LogInformation("[Playwright] Disposing browser session");
        if (_page    is not null) await _page.CloseAsync();
        if (_browser is not null) await _browser.CloseAsync();
        _playwright?.Dispose();
        _lock.Dispose();
    }

    private sealed class LockReleaser(SemaphoreSlim sem) : IDisposable
    {
        public void Dispose() => sem.Release();
    }
}

public sealed record BrowserSessionStatus(
    bool    IsLaunched,
    bool    HasActivePage,
    string? CurrentUrl,
    int     NavigationTimeoutMs,
    int     ActionTimeoutMs,
    bool    Headless
);

/// <summary>
/// EC-PW-002 fix. Records the outcome of the most recent browser-triggered
/// download. SavedPath is null and Error is set if capture itself failed
/// (e.g. PW-LAW-007 sandbox rejection) — callers must check both, not assume
/// a non-null record means the file exists on disk.
/// </summary>
public sealed record DownloadRecord(
    string?  SuggestedFilename,
    string?  SavedPath,
    long     Bytes,
    string   Url,
    DateTime CapturedAtUtc,
    string?  Error = null
);
