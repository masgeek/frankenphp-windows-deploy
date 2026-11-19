using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PhpManager;

/// <summary>
/// Remembers the real PHP version of each install folder in <c>&lt;BasePath&gt;\.php-installations</c> so folders can be
/// named anything (not just 8.4.27) without running <c>php --version</c> on every refresh. An entry is reused only
/// while php.exe is unchanged (same size and write time).
/// </summary>
public static class PhpInstallationCache
{
    public const string FileName = ".php-installations";

    private sealed class Entry
    {
        public string Version { get; set; } = "";
        public long Stamp { get; set; }
    }

    public static string FilePath(string basePath) => Path.Combine(basePath, FileName);

    /// <summary>Version of the PHP in <paramref name="installDir"/>, from the cache or by running php.exe; null if unreadable.</summary>
    public static string? GetVersion(string basePath, string installDir, Func<string, string?> readVersionOutput) =>
        GetVersions(basePath, [installDir], readVersionOutput).GetValueOrDefault(installDir);

    /// <summary>Resolves several folders with a single cache load/save.</summary>
    public static Dictionary<string, string?> GetVersions(
        string basePath, IEnumerable<string> installDirs, Func<string, string?> readVersionOutput)
    {
        var cache = Load(basePath);
        var dirty = false;
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var dir in installDirs)
        {
            var exe = Path.Combine(dir, "php.exe");
            var key = Path.GetFileName(dir.TrimEnd('\\'));
            if (!File.Exists(exe))
            {
                result[dir] = null;
                continue;
            }

            var stamp = Stamp(exe);
            if (cache.TryGetValue(key, out var hit) && hit.Stamp == stamp && !string.IsNullOrEmpty(hit.Version))
            {
                result[dir] = hit.Version;
                continue;
            }

            string? version = null;
            try
            {
                var m = Regex.Match(readVersionOutput(exe) ?? "", @"\d+\.\d+\.\d+");
                if (m.Success)
                    version = m.Value;
            }
            catch
            {
                // php.exe could not be run; leave the version unknown
            }

            result[dir] = version;
            if (version != null)
            {
                cache[key] = new Entry { Version = version, Stamp = stamp };
                dirty = true;
            }
        }

        if (dirty)
            Save(basePath, cache);

        return result;
    }

    private static long Stamp(string exe)
    {
        var info = new FileInfo(exe);
        return HashCode.Combine(info.LastWriteTimeUtc.Ticks, info.Length);
    }

    private static Dictionary<string, Entry> Load(string basePath)
    {
        try
        {
            var file = FilePath(basePath);
            if (File.Exists(file))
            {
                var loaded = JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(file));
                if (loaded != null)
                    return new Dictionary<string, Entry>(loaded, StringComparer.OrdinalIgnoreCase);
            }
        }
        catch
        {
            // corrupt cache: rebuild it
        }

        return new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
    }

    private static void Save(string basePath, Dictionary<string, Entry> cache)
    {
        try
        {
            Directory.CreateDirectory(basePath);
            File.WriteAllText(FilePath(basePath),
                JsonSerializer.Serialize(cache, new JsonSerializerOptions { WriteIndented = true }),
                new UTF8Encoding(false));
        }
        catch
        {
            // read-only location: fall back to running php.exe each time
        }
    }
}
