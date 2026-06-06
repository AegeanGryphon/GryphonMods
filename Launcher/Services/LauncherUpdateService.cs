using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace Launcher.Services;

/// <summary>
/// Represents a launcher update zip that has been downloaded and is ready
/// to be installed.
/// </summary>
public record StagedUpdate(string ZipPath, string Version, Version ParsedVersion);

/// <summary>
/// Orchestrates launcher self-updates in two phases:
///
///   1. CheckAndStageAsync — called on startup. Downloads the latest release
///      zip into the launcher's own directory (silently, in the background).
///      If a staged zip already exists and is current, skips the download.
///      If a staged zip exists but a newer release is available, deletes it
///      and downloads the newer one.
///
///   2. ApplyUpdateAsync — called when the user clicks "Install &amp; Restart".
///      Writes a PowerShell helper script that waits for this process to exit,
///      unzips the staged file over the launcher directory, launches the new
///      exe, then cleans up. The caller must shut down the application after
///      this returns.
/// </summary>
public class LauncherUpdateService
{
    private const string ReleasesApiUrl =
        "https://api.github.com/repos/AegeanGryphon/GryphonMods/releases";

    private const string DownloadUrlTemplate =
        "https://github.com/AegeanGryphon/GryphonMods/releases/download/Launcher-v{0}/LumenTaleLauncher.zip";

    // Staged zips live next to the launcher exe with this naming scheme.
    private const string StagedPrefix = "LumenTaleLauncher_staged_v";
    private const string StagedSuffix = ".zip";

    private static readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(60),
        DefaultRequestHeaders = { { "User-Agent", "LumenTaleLauncher" } }
    };

    // ── Installed version ──────────────────────────────────────────────────────

    public static Version GetInstalledVersion()
    {
        var v = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);
        return new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
    }

    // ── Directory helpers ──────────────────────────────────────────────────────

    private static string ExeDir =>
        Path.GetDirectoryName(
            Process.GetCurrentProcess().MainModule?.FileName
            ?? Assembly.GetExecutingAssembly().Location)!;

    private static string StagedZipPath(string version) =>
        Path.Combine(ExeDir, $"{StagedPrefix}{version}{StagedSuffix}");

    // ── Staged update discovery ────────────────────────────────────────────────

    /// <summary>
    /// Scans the launcher directory for a previously staged update zip.
    /// Returns the first valid one found, or null.
    /// </summary>
    public StagedUpdate? FindStagedUpdate()
    {
        foreach (var file in Directory.GetFiles(ExeDir, $"{StagedPrefix}*{StagedSuffix}"))
        {
            var name   = Path.GetFileNameWithoutExtension(file);
            var verStr = name[StagedPrefix.Length..];
            if (Version.TryParse(verStr, out var ver))
                return new StagedUpdate(file, verStr, ver);
        }
        return null;
    }

    // ── GitHub version check ───────────────────────────────────────────────────

    /// <summary>
    /// Fetches all releases and returns the latest "Launcher-v*" tag's version
    /// string, or null if none are found / network fails.
    /// </summary>
    public async Task<(string? VersionStr, Version? Version)> GetLatestVersionAsync()
    {
        var json      = await _http.GetStringAsync(ReleasesApiUrl);
        using var doc = JsonDocument.Parse(json);

        Version? latestVer = null;
        string?  latestStr = null;

        foreach (var release in doc.RootElement.EnumerateArray())
        {
            var tag = release.GetProperty("tag_name").GetString() ?? "";
            if (!tag.StartsWith("Launcher-v", StringComparison.OrdinalIgnoreCase)) continue;

            var verStr = tag["Launcher-v".Length..];
            if (!Version.TryParse(verStr, out var ver)) continue;

            if (latestVer is null || ver > latestVer)
            {
                latestVer = ver;
                latestStr = verStr;
            }
        }

        return (latestStr, latestVer);
    }

    // ── Phase 1 — Check & Stage ────────────────────────────────────────────────

    /// <summary>
    /// Checks for a newer release, downloads it if needed, and returns a
    /// <see cref="StagedUpdate"/> that is ready to install. Returns null if
    /// the launcher is already up to date.
    ///
    /// <paramref name="onProgress"/> is invoked on the calling thread via
    /// the supplied dispatcher when status text should change.
    /// </summary>
    public async Task<StagedUpdate?> CheckAndStageAsync(Action<string> onProgress)
    {
        var installed              = GetInstalledVersion();
        var (latestStr, latestVer) = await GetLatestVersionAsync();

        if (latestStr is null || latestVer is null) return null;
        if (latestVer <= installed)
        {
            // Also clean up any leftover staged zip from a previous update.
            CleanStagedZipsOlderThan(installed);
            return null;
        }

        // Is there already a staged zip?
        var staged = FindStagedUpdate();

        if (staged is not null)
        {
            if (staged.ParsedVersion >= latestVer)
            {
                // Already have the latest staged — nothing to download.
                return staged;
            }

            // Staged zip is outdated — delete it before downloading the newer one.
            TryDelete(staged.ZipPath);
        }

        // Download the new version.
        onProgress($"Downloading launcher v{latestStr}…");

        var zipPath = StagedZipPath(latestStr);
        var bytes   = await _http.GetByteArrayAsync(string.Format(DownloadUrlTemplate, latestStr));
        await File.WriteAllBytesAsync(zipPath, bytes);

        return new StagedUpdate(zipPath, latestStr, latestVer);
    }

    // ── Phase 2 — Apply ───────────────────────────────────────────────────────

    /// <summary>
    /// Writes a PowerShell helper script and launches it. The script waits for
    /// this process to exit, extracts <paramref name="staged"/> over the
    /// launcher directory, starts the new exe, then cleans up both the zip
    /// and itself. The caller must call <c>Application.Current.Shutdown()</c>
    /// after this returns.
    /// </summary>
    public async Task ApplyUpdateAsync(StagedUpdate staged, Action<string> onProgress)
    {
        var exePath    = Process.GetCurrentProcess().MainModule?.FileName
                         ?? Assembly.GetExecutingAssembly().Location;
        var exeDir     = Path.GetDirectoryName(exePath)!;
        var exeName    = Path.GetFileName(exePath);
        var scriptPath = Path.Combine(Path.GetTempPath(), "LumenTaleLauncher_updater.ps1");

        onProgress("Preparing updater…");

        // Plain raw string — PowerShell's $, { } are NOT interpolated by C#.
        // Values are passed as named parameters when launching powershell.exe.
        var script = """
            param([int]$Pid, [string]$Zip, [string]$Dir, [string]$Exe)
            $p = Get-Process -Id $Pid -ErrorAction SilentlyContinue
            while ($p -and !$p.HasExited) {
                Start-Sleep -Milliseconds 300
                $p = Get-Process -Id $Pid -ErrorAction SilentlyContinue
            }
            Start-Sleep -Seconds 1
            Add-Type -AssemblyName System.IO.Compression.FileSystem
            [System.IO.Compression.ZipFile]::ExtractToDirectory($Zip, $Dir, $true)
            Start-Process -FilePath (Join-Path $Dir $Exe)
            Remove-Item $Zip  -Force -ErrorAction SilentlyContinue
            Remove-Item $MyInvocation.MyCommand.Path -Force -ErrorAction SilentlyContinue
            """;

        await File.WriteAllTextAsync(scriptPath, script);

        onProgress("Launching updater — restarting…");

        Process.Start(new ProcessStartInfo
        {
            FileName        = "powershell.exe",
            Arguments       = $"-ExecutionPolicy Bypass -File \"{scriptPath}\" " +
                              $"-Pid {Environment.ProcessId} " +
                              $"-Zip \"{staged.ZipPath}\" " +
                              $"-Dir \"{exeDir}\" " +
                              $"-Exe \"{exeName}\"",
            UseShellExecute = false,
            CreateNoWindow  = true,
            WindowStyle     = ProcessWindowStyle.Hidden
        });
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private void CleanStagedZipsOlderThan(Version threshold)
    {
        foreach (var file in Directory.GetFiles(ExeDir, $"{StagedPrefix}*{StagedSuffix}"))
        {
            var name   = Path.GetFileNameWithoutExtension(file);
            var verStr = name[StagedPrefix.Length..];
            if (Version.TryParse(verStr, out var ver) && ver <= threshold)
                TryDelete(file);
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* best-effort */ }
    }
}
