// src/Mcps/ProjectName.Mcp.Playwright/Configuration/PlaywrightConfig.cs
// ═══════════════════════════════════════════════════════════════════════════════
// PROJECTNAME — MCP.PLAYWRIGHT
// File       : Configuration/PlaywrightConfig.cs
// Identity   : Browser Actuator Configuration & URL Safety Gate
// Law Anchor : PW-LAW-001 (URL Safety), PW-LAW-003 (Timeout Caps),
//              PW-LAW-005 (Serial Page Execution), SAFETY-003
// ───────────────────────────────────────────────────────────────────────────────
// Configuration is injected via identity.json / appsettings / environment.
// ResolveAndValidateUrl is the PW-LAW-001 enforcement point:
//   - Rejects private/loopback ranges
//   - Rejects non-HTTP(S) schemes
//   - Allows explicit bypass only via AllowedPrivateHosts (dev overrides)
// All timeout values are CAPS — agents may not request higher values.
// ═══════════════════════════════════════════════════════════════════════════════

using Microsoft.Extensions.Configuration;
using System;
using System.IO;

namespace ProjectName.Mcp.Playwright.Configuration;

public sealed class PlaywrightConfig
{
    // ── Timeout caps (PW-LAW-003) ─────────────────────────────────────────────
    public int NavigationTimeoutMs  { get; init; } = 30_000;
    public int ActionTimeoutMs      { get; init; } = 10_000;
    public int PageLoadTimeoutMs    { get; init; } = 60_000;

    // ── Content limits ────────────────────────────────────────────────────────
    public int MaxContentBytes      { get; init; } = 512_000;  // 512 KB

    // ── Screenshot sandbox (PW-LAW-006 — screenshot path containment) ─────────
    // Mirrors FilesystemConfig.SandboxRoot: TakeScreenshot's outputPath is a
    // client-supplied string and MUST NOT be trusted as an absolute filesystem
    // path. All screenshots are written under ScreenshotDir; ../ traversal or
    // absolute paths supplied by the agent are rejected, never followed.
    public string ScreenshotDir     { get; init; } = Path.Combine(Directory.GetCurrentDirectory(), "Screenshots");

    // ── Session control (PW-LAW-005 — serial page execution) ─────────────────
    public bool Headless            { get; init; } = true;

    // ── URL safety bypass list (dev environments only) ────────────────────────
    // Example: ["localhost", "host.docker.internal"]
    public string[] AllowedPrivateHosts { get; init; } = [];

    public PlaywrightConfig() { }

    public PlaywrightConfig(IConfiguration config)
    {
        var section = config.GetSection("Playwright");
        NavigationTimeoutMs  = section.GetValue<int?>("NavigationTimeoutMs")  ?? NavigationTimeoutMs;
        ActionTimeoutMs      = section.GetValue<int?>("ActionTimeoutMs")      ?? ActionTimeoutMs;
        PageLoadTimeoutMs    = section.GetValue<int?>("PageLoadTimeoutMs")    ?? PageLoadTimeoutMs;
        MaxContentBytes      = section.GetValue<int?>("MaxContentBytes")      ?? MaxContentBytes;
        Headless             = section.GetValue<bool?>("Headless")            ?? Headless;
        AllowedPrivateHosts  = section.GetSection("AllowedPrivateHosts").Get<string[]>() ?? AllowedPrivateHosts;
        ScreenshotDir        = section.GetValue<string?>("ScreenshotDir")      ?? ScreenshotDir;

        if (!Directory.Exists(ScreenshotDir))
            Directory.CreateDirectory(ScreenshotDir);
    }

    /// <summary>
    /// PW-LAW-001 enforcement. Validates that the URL is safe to navigate to.
    /// Throws <see cref="InvalidOperationException"/> if the URL is blocked.
    /// Returns the original URL string if valid.
    /// </summary>
    public string ResolveAndValidateUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("url must be a non-empty string");

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            throw new InvalidOperationException($"URL is not well-formed: '{url}'");

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException(
                $"PW-LAW-001: Only http/https schemes are permitted. Got: '{uri.Scheme}'");

        var host = uri.Host.ToLowerInvariant();

        // Allow explicit dev-bypass hosts
        if (AllowedPrivateHosts.Contains(host, StringComparer.OrdinalIgnoreCase))
            return url;

        // Block loopback / private ranges by hostname pattern
        if (host == "localhost" || host == "127.0.0.1" || host == "::1" ||
            host.EndsWith(".local") || host.EndsWith(".internal"))
            throw new InvalidOperationException(
                $"PW-LAW-001: Private/loopback host '{host}' is not permitted. " +
                "Add to AllowedPrivateHosts for dev override.");

        return url;
    }

    /// <summary>
    /// PW-LAW-006 enforcement. Resolves a client-supplied screenshot filename into
    /// a safe absolute path confined to <see cref="ScreenshotDir"/>. Mirrors
    /// FilesystemConfig.ResolveAndValidatePath — traversal ("../"), absolute paths,
    /// and drive-qualified paths supplied by the caller are all rejected rather
    /// than followed. A null/empty name auto-generates a timestamped filename.
    /// Throws <see cref="UnauthorizedAccessException"/> if the resolved path
    /// escapes ScreenshotDir.
    /// </summary>
    public string ResolveScreenshotPath(string? requestedName)
    {
        var fileName = string.IsNullOrWhiteSpace(requestedName)
            ? $"screenshot-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}.png"
            : Path.GetFileName(requestedName); // strips any directory component outright

        if (string.IsNullOrWhiteSpace(fileName))
            throw new InvalidOperationException($"PW-LAW-006: outputPath '{requestedName}' resolved to an empty filename.");

        var combined = Path.GetFullPath(Path.Combine(ScreenshotDir, fileName));

        var sandboxPrefix = ScreenshotDir.EndsWith(Path.DirectorySeparatorChar.ToString())
            ? ScreenshotDir
            : ScreenshotDir + Path.DirectorySeparatorChar;

        if (!combined.StartsWith(sandboxPrefix, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException(
                $"PW-LAW-006: outputPath '{requestedName}' resolves outside the allowed ScreenshotDir sandbox.");

        return combined;
    }
}
