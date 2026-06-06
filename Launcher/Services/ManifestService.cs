using System.Net.Http;
using System.Text.Json;
using Launcher.Models;

namespace Launcher.Services;

public class ManifestService
{
    private const string ManifestUrl =
        "https://raw.githubusercontent.com/AegeanGryphon/GryphonMods/main/manifest.json";

    private const string BetaManifestUrl =
        "https://raw.githubusercontent.com/AegeanGryphon/GryphonMods/main/beta-manifest.json";

    private static readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <param name="isBeta">When true, fetches the beta manifest (restricted access mods).</param>
    public async Task<Manifest> FetchAsync(bool isBeta = false)
    {
        var url  = isBeta ? ManifestUrl : BetaManifestUrl;
        var json = await _http.GetStringAsync(url);
        var manifest = JsonSerializer.Deserialize<Manifest>(json, _jsonOptions)
                       ?? throw new InvalidOperationException("Manifest deserialized to null.");
        return manifest;
    }
}
