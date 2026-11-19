using System.IO;
using System.Text.Json;

namespace PhpManager;

/// <summary>
/// Everything needed to set up a second machine the same way: paths, download URLs, the active PHP's
/// php.ini directives and extension choices.
/// </summary>
public sealed class SettingsBundle
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;
    public string ExportedAt { get; set; } = "";
    public string BasePath { get; set; } = "";
    public string FrankenPhpPath { get; set; } = "";

    /// <summary>The URL table, in the format of UrlDatabase.ExportJson.</summary>
    public string UrlsJson { get; set; } = "";

    public string PhpVersion { get; set; } = "";
    public Dictionary<string, string> Directives { get; set; } = new();
    public List<string> EnabledExtensions { get; set; } = [];
    public List<string> DisabledExtensions { get; set; } = [];
    public bool OpcacheLoaded { get; set; }

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    /// <summary>Parses and validates a bundle; throws <see cref="InvalidDataException"/> with a readable message.</summary>
    public static SettingsBundle FromJson(string json)
    {
        SettingsBundle? bundle;
        try
        {
            bundle = JsonSerializer.Deserialize<SettingsBundle>(json);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Not a valid settings file: {ex.Message}");
        }

        if (bundle == null)
            throw new InvalidDataException("The settings file is empty.");
        if (bundle.Version < 1 || bundle.Version > CurrentVersion)
            throw new InvalidDataException(
                $"This settings file is version {bundle.Version}; this app understands up to version {CurrentVersion}. Update PHP Manager.");

        foreach (var (key, value) in bundle.Directives)
        {
            if (!IniSettingDefs.IsValidKey(key))
                throw new InvalidDataException($"Invalid directive name '{key}'.");
            if (value.Contains('\n') || value.Contains('\r'))
                throw new InvalidDataException($"Directive '{key}' has a multi-line value.");
        }

        return bundle;
    }
}
