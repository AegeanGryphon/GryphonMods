using Microsoft.Win32;
using System.IO;

namespace Launcher.Services;

public class GameLocator
{
    private const string GameFolderName = "LumenTale Memories of Trey";
    private const string BepInExMarker  = @"BepInEx\core\BepInEx.Core.dll";

    public string? FindGamePath()
    {
        // 1. Try Steam install paths from registry
        foreach (var steamPath in GetSteamPaths())
        {
            // Parse libraryfolders.vdf for all library roots
            var vdfPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
            var libraryRoots = ParseLibraryRoots(vdfPath);

            // Also check the Steam install path itself
            libraryRoots.Add(steamPath);

            foreach (var root in libraryRoots)
            {
                var candidate = Path.Combine(root, "steamapps", "common", GameFolderName);
                if (Directory.Exists(candidate))
                    return candidate;
            }
        }

        return null;
    }

    public bool IsBepInExInstalled(string gamePath)
        => File.Exists(Path.Combine(gamePath, BepInExMarker));

    // ── Private helpers ───────────────────────────────────────────────────────

    private static IEnumerable<string> GetSteamPaths()
    {
        var paths = new List<string>();

        // HKLM 32-bit node
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Wow6432Node\Valve\Steam");
            if (key?.GetValue("InstallPath") is string p1 && !string.IsNullOrEmpty(p1))
                paths.Add(p1);
        }
        catch { }

        // HKCU
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Valve\Steam");
            if (key?.GetValue("SteamPath") is string p2 && !string.IsNullOrEmpty(p2))
                paths.Add(p2);
        }
        catch { }

        return paths.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static List<string> ParseLibraryRoots(string vdfPath)
    {
        var result = new List<string>();
        if (!File.Exists(vdfPath)) return result;

        try
        {
            foreach (var line in File.ReadAllLines(vdfPath))
            {
                var trimmed = line.Trim();
                // Lines look like:   "path"   "D:\\SteamLibrary"
                if (trimmed.Contains("\"path\"", StringComparison.OrdinalIgnoreCase))
                {
                    // Extract the second quoted value
                    var parts = trimmed.Split('"', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2)
                    {
                        var path = parts[^1].Replace("\\\\", "\\");
                        if (!string.IsNullOrWhiteSpace(path))
                            result.Add(path);
                    }
                }
            }
        }
        catch { }

        return result;
    }
}
