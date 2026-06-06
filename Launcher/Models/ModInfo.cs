using System.Text.Json.Serialization;

namespace Launcher.Models;

public record ManifestMod(
    [property: JsonPropertyName("id")]          string? Id,
    [property: JsonPropertyName("name")]        string Name,
    [property: JsonPropertyName("author")]      string Author,
    [property: JsonPropertyName("version")]     string Version,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("downloadUrl")] string DownloadUrl,
    [property: JsonPropertyName("fileName")]    string FileName,
    [property: JsonPropertyName("changelog")]   string? Changelog
);

public record Manifest(
    [property: JsonPropertyName("mods")] List<ManifestMod> Mods
);

public record InstalledMod(
    string Name,
    string Version,
    string FilePath
);
