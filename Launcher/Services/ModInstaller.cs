using System.IO;
using System.Net.Http;

namespace Launcher.Services;

public class ModInstaller
{
    private static readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(60)
    };

    /// <summary>
    /// Downloads a mod DLL from <paramref name="downloadUrl"/> and saves it to
    /// BepInEx\plugins\ inside the game folder.
    /// </summary>
    public async Task InstallAsync(string downloadUrl, string fileName, string gamePath)
    {
        if (string.IsNullOrWhiteSpace(downloadUrl))
            throw new ArgumentException("Download URL is empty.", nameof(downloadUrl));

        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("File name is empty.", nameof(fileName));

        var pluginsDir = Path.Combine(gamePath, "BepInEx", "plugins");
        Directory.CreateDirectory(pluginsDir);

        var destPath = Path.Combine(pluginsDir, fileName);

        var bytes = await _http.GetByteArrayAsync(downloadUrl);
        await File.WriteAllBytesAsync(destPath, bytes);
    }
}
