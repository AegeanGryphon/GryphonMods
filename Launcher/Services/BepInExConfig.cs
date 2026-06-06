using System.IO;

namespace Launcher.Services;

public static class BepInExConfig
{
    private const string Section = "[Logging.Console]";
    private const string Key     = "Enabled";

    public static bool GetConsoleEnabled(string gamePath)
    {
        var path = CfgPath(gamePath);
        if (!File.Exists(path)) return false; // not yet generated — default is hidden

        bool inSection = false;
        foreach (var line in File.ReadAllLines(path))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("[")) inSection = trimmed == Section;
            if (!inSection) continue;
            if (trimmed.StartsWith(Key, StringComparison.OrdinalIgnoreCase) && trimmed.Contains('='))
            {
                var value = trimmed.Split('=', 2)[1].Trim();
                return value.Equals("true", StringComparison.OrdinalIgnoreCase);
            }
        }
        return false;
    }

    public static void SetConsoleEnabled(string gamePath, bool enabled)
    {
        var path = CfgPath(gamePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        if (!File.Exists(path))
        {
            // Create minimal config with just the console setting
            File.WriteAllText(path,
                $"[Logging.Console]\r\nEnabled = {(enabled ? "true" : "false")}\r\n");
            return;
        }

        var lines    = File.ReadAllLines(path);
        bool inSection = false, written = false;

        for (int i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.StartsWith("[")) inSection = trimmed == Section;
            if (inSection && trimmed.StartsWith(Key, StringComparison.OrdinalIgnoreCase) && trimmed.Contains('='))
            {
                lines[i] = $"{Key} = {(enabled ? "true" : "false")}";
                written = true;
            }
        }

        if (!written)
        {
            // Section exists but key is missing — append key after section header
            var list = lines.ToList();
            int idx  = list.FindIndex(l => l.Trim() == Section);
            list.Insert(idx + 1, $"{Key} = {(enabled ? "true" : "false")}");
            File.WriteAllLines(path, list);
            return;
        }

        File.WriteAllLines(path, lines);
    }

    private static string CfgPath(string gamePath) =>
        Path.Combine(gamePath, "BepInEx", "config", "BepInEx.cfg");
}
