using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PhpManager;

/// <summary>Works out which installed PHP a project folder needs, from .php-version or composer.json.</summary>
public static class ProjectPhp
{
    public sealed record Requirement(string Source, string Constraint);

    /// <summary>
    /// Reads the requirement. Order: .php-version, then composer.json config.platform.php (an explicit override),
    /// then require.php. Returns null when the folder states no requirement.
    /// </summary>
    public static Requirement? ReadRequirement(string folder)
    {
        var versionFile = Path.Combine(folder, ".php-version");
        if (File.Exists(versionFile))
        {
            var text = File.ReadAllText(versionFile).Trim();
            if (Regex.IsMatch(text, @"^\d+(\.\d+){0,2}$"))
                return new Requirement(".php-version", text);
        }

        var composer = Path.Combine(folder, "composer.json");
        if (!File.Exists(composer))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(composer));
            var root = doc.RootElement;

            if (root.TryGetProperty("config", out var config) &&
                config.ValueKind == JsonValueKind.Object &&
                config.TryGetProperty("platform", out var platform) &&
                platform.ValueKind == JsonValueKind.Object &&
                platform.TryGetProperty("php", out var platformPhp) &&
                platformPhp.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(platformPhp.GetString()))
            {
                return new Requirement("composer.json (config.platform.php)", platformPhp.GetString()!);
            }

            if (root.TryGetProperty("require", out var require) &&
                require.ValueKind == JsonValueKind.Object &&
                require.TryGetProperty("php", out var php) &&
                php.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(php.GetString()))
            {
                return new Requirement("composer.json (require.php)", php.GetString()!);
            }
        }
        catch (JsonException)
        {
            // unreadable composer.json: treat as no requirement
        }

        return null;
    }

    /// <summary>Installed versions that satisfy the requirement, highest first.</summary>
    public static List<string> Matching(string constraint, IEnumerable<string> installed) =>
        installed
            .Select(v => (Text: v, Parsed: Version.TryParse(v, out var parsed) ? parsed : null))
            .Where(x => x.Parsed != null && ComposerConstraint.Satisfies(constraint, x.Parsed!))
            .OrderByDescending(x => x.Parsed)
            .Select(x => x.Text)
            .ToList();

    /// <summary>A .cmd shim that runs a specific php.exe; dropped in a project folder it pins PHP for that folder.</summary>
    public static string ShimContent(string phpExe) =>
        $"@echo off\r\n\"{phpExe}\" %*\r\n";

    /// <summary>Writes .php-version and php.cmd into the folder.</summary>
    public static void WritePin(string folder, string version, string phpExe)
    {
        File.WriteAllText(Path.Combine(folder, ".php-version"), version + "\n", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(folder, "php.cmd"), ShimContent(phpExe), new UTF8Encoding(false));
    }
}
