using System.IO;

namespace Launcher.Services;

public static class DotNetChecker
{
    /// <summary>
    /// Returns the highest installed .NET Desktop Runtime 10.x version string,
    /// or null if none is found (shouldn't happen while this process is running,
    /// but we check the well-known directory for completeness).
    /// </summary>
    public static string? GetInstalledVersion()
    {
        // Primary location on x64 Windows
        var baseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "dotnet", "shared", "Microsoft.WindowsDesktop.App");

        if (!Directory.Exists(baseDir))
        {
            // Fall back to x86 install or arm64
            baseDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "dotnet", "shared", "Microsoft.WindowsDesktop.App");
        }

        if (!Directory.Exists(baseDir))
            return null;

        var v10 = Directory.GetDirectories(baseDir, "10.*")
                            .Select(d => Path.GetFileName(d))
                            .Where(n => Version.TryParse(n, out _))
                            .OrderByDescending(n => Version.Parse(n))
                            .FirstOrDefault();

        return v10;
    }
}
