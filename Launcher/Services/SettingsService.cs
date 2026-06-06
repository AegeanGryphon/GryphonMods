using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Launcher.Services;

/// <summary>
/// Persists launcher settings to %LocalAppData%\LumenTaleLauncher\settings.ini.
/// Beta access is validated locally via a SHA-256 hash of the token — the token
/// itself is never transmitted or stored after validation.
/// </summary>
public class SettingsService
{
    // SHA-256 of "K7mX9nP4Q2rL5vT" (UTF-8, lowercase hex)
    private const string BetaTokenHash =
        "9bc552dacda8ea5022fa79f076b12123c346b5c9b2cb3ba6212cbb41783c3b05";

    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LumenTaleLauncher");

    private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.ini");

    public bool IsBetaEnabled { get; private set; }

    // ── Persistence ───────────────────────────────────────────────────────────

    public void Load()
    {
        if (!File.Exists(SettingsPath)) return;

        foreach (var line in File.ReadAllLines(SettingsPath))
        {
            var parts = line.Split('=', 2);
            if (parts.Length != 2) continue;

            var key   = parts[0].Trim();
            var value = parts[1].Trim();

            if (key.Equals("betaEnabled", StringComparison.OrdinalIgnoreCase))
                IsBetaEnabled = value.Equals("true", StringComparison.OrdinalIgnoreCase);
        }
    }

    private void Save()
    {
        Directory.CreateDirectory(SettingsDir);
        File.WriteAllLines(SettingsPath, new[]
        {
            $"betaEnabled={IsBetaEnabled.ToString().ToLowerInvariant()}"
        });
    }

    // ── Beta access ───────────────────────────────────────────────────────────

    /// <summary>
    /// Returns true if the provided token matches the hardcoded hash.
    /// On success, persists the enabled state for future sessions.
    /// </summary>
    public bool ValidateAndEnableBeta(string token)
    {
        var hash = ComputeHash(token.Trim());
        if (!hash.Equals(BetaTokenHash, StringComparison.OrdinalIgnoreCase))
            return false;

        IsBetaEnabled = true;
        Save();
        return true;
    }

    public void DisableBeta()
    {
        IsBetaEnabled = false;
        Save();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string ComputeHash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
