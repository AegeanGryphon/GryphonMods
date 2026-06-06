using System.IO;
using System.IO.Compression;
using System.Net.Http;

namespace Launcher.Services;

public class BepInExInstaller
{
    private const string BepInExUrl =
        "https://github.com/AegeanGryphon/GryphonMods/releases/download/Bepinex-v.6.0.0-be.755/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.755+3fab71a.zip";

    private static readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromMinutes(5)
    };

    /// <summary>
    /// Downloads and extracts BepInEx into <paramref name="gamePath"/>.
    /// Reports progress via <paramref name="onProgress"/>.
    /// </summary>
    public async Task InstallAsync(string gamePath, Action<string> onProgress)
    {
        onProgress("Downloading BepInEx…");

        var zipBytes = await _http.GetByteArrayAsync(BepInExUrl);

        onProgress("Extracting BepInEx…");

        using var ms     = new MemoryStream(zipBytes);
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read);

        foreach (var entry in archive.Entries)
        {
            // Skip directory entries (empty Name segment)
            if (string.IsNullOrEmpty(entry.Name))
                continue;

            // Preserve the full relative path from the archive
            var destPath = Path.Combine(gamePath, entry.FullName
                .Replace('/', Path.DirectorySeparatorChar));

            // Ensure destination directory exists
            var destDir = Path.GetDirectoryName(destPath);
            if (destDir is not null)
                Directory.CreateDirectory(destDir);

            onProgress($"Extracting {entry.FullName}");

            entry.ExtractToFile(destPath, overwrite: true);
        }

        onProgress("BepInEx installed.");
    }
}
