using System.IO;

namespace PhpManager;

public sealed record PhpUpdate(string Installed, string Latest)
{
    public string Display => $"{Installed}  ->  {Latest}";
}

/// <summary>Finds newer patch releases of the PHP lines you already have installed.</summary>
public static class UpdateChecker
{
    /// <summary>For each installed x.y.z, the highest available x.y.z with the same major.minor, if newer.</summary>
    public static List<PhpUpdate> FindUpdates(IEnumerable<string> installed, IEnumerable<string> available)
    {
        var catalog = available
            .Select(v => Version.TryParse(v, out var parsed) ? (Text: v, Parsed: parsed) : default)
            .Where(x => x.Parsed != null)
            .ToList();

        var updates = new List<PhpUpdate>();
        foreach (var current in installed)
        {
            if (!Version.TryParse(current, out var cur) || cur.Build < 0)
                continue;

            var best = catalog
                .Where(c => c.Parsed!.Major == cur.Major && c.Parsed.Minor == cur.Minor && c.Parsed > cur)
                .OrderByDescending(c => c.Parsed)
                .FirstOrDefault();

            if (best.Parsed != null)
                updates.Add(new PhpUpdate(current, best.Text));
        }

        return updates;
    }

    /// <summary>
    /// Names of files in the old ext folder that the new install does not have (extensions you added later, such as
    /// redis or xdebug). They are carried over on upgrade; patch releases keep the same extension ABI.
    /// </summary>
    public static List<string> MissingExtensionFiles(string oldExtDir, string newExtDir)
    {
        if (!Directory.Exists(oldExtDir))
            return [];

        return Directory.GetFiles(oldExtDir)
            .Select(Path.GetFileName)
            .Where(n => n != null && !File.Exists(Path.Combine(newExtDir, n)))
            .Select(n => n!)
            .ToList();
    }
}
