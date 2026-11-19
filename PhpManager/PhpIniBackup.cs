using System.Globalization;
using System.IO;
using System.Text;

namespace PhpManager;

/// <summary>
/// Timestamped php.ini backups kept in <c>.php-ini-backups</c> next to the ini. Every modification or replacement
/// goes through <see cref="Write"/> / <see cref="Copy"/>, which back up first, so any change can be rolled back.
/// </summary>
public static class PhpIniBackup
{
    public const string FolderName = ".php-ini-backups";
    public const int MaxBackups = 25;
    private const string Prefix = "php.ini.";
    private const string Suffix = ".bak";
    private const string Stamp = "yyyyMMdd-HHmmss-fff";

    public sealed record BackupInfo(string Path, DateTime Created, long Size)
    {
        public string Display => $"{Created:yyyy-MM-dd HH:mm:ss}  ({Size / 1024.0:0.#} KB)";
    }

    public static string FolderFor(string iniPath) =>
        Path.Combine(Path.GetDirectoryName(iniPath) ?? "", FolderName);

    /// <summary>Copies the current php.ini into the backup folder. Returns null if there was nothing to back up or it is unchanged since the last backup.</summary>
    public static string? Backup(string iniPath, DateTime? now = null)
    {
        if (!File.Exists(iniPath))
            return null;

        var current = File.ReadAllBytes(iniPath);
        var existing = List(iniPath);
        if (existing.Count > 0 && File.ReadAllBytes(existing[0].Path).AsSpan().SequenceEqual(current))
            return null;

        var folder = FolderFor(iniPath);
        Directory.CreateDirectory(folder);

        var target = Path.Combine(folder, Prefix + (now ?? DateTime.Now).ToString(Stamp, CultureInfo.InvariantCulture) + Suffix);
        for (var i = 1; File.Exists(target); i++) // same millisecond: keep both
            target = Path.Combine(folder, $"{Prefix}{(now ?? DateTime.Now).ToString(Stamp, CultureInfo.InvariantCulture)}-{i}{Suffix}");

        File.WriteAllBytes(target, current);
        Prune(iniPath);
        return target;
    }

    /// <summary>Backups, newest first.</summary>
    public static List<BackupInfo> List(string iniPath)
    {
        var folder = FolderFor(iniPath);
        if (!Directory.Exists(folder))
            return [];

        return Directory.GetFiles(folder, Prefix + "*" + Suffix)
            .Select(f => new BackupInfo(f, ParseTime(f), new FileInfo(f).Length))
            .OrderByDescending(b => b.Created)
            .ThenByDescending(b => b.Path, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Restores a backup over php.ini. The current file is backed up first, so the rollback itself can be undone.
    /// </summary>
    public static void Restore(string iniPath, string backupPath)
    {
        var folder = Path.GetFullPath(FolderFor(iniPath)).TrimEnd('\\') + "\\";
        var full = Path.GetFullPath(backupPath);
        if (!full.StartsWith(folder, StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
            throw new FileNotFoundException("That backup is not in this PHP's backup folder.", backupPath);

        var bytes = File.ReadAllBytes(full);
        Backup(iniPath);
        File.WriteAllBytes(iniPath, bytes);
    }

    /// <summary>Backs up, then writes <paramref name="content"/> to php.ini.</summary>
    public static void Write(string iniPath, string content)
    {
        Backup(iniPath);
        File.WriteAllText(iniPath, content, new UTF8Encoding(false));
    }

    /// <summary>Backs up, then replaces php.ini with a copy of <paramref name="source"/>.</summary>
    public static void Copy(string source, string iniPath)
    {
        Backup(iniPath);
        File.Copy(source, iniPath, overwrite: true);
    }

    private static void Prune(string iniPath)
    {
        foreach (var old in List(iniPath).Skip(MaxBackups))
        {
            try { File.Delete(old.Path); } catch { /* best effort */ }
        }
    }

    private static DateTime ParseTime(string file)
    {
        var name = Path.GetFileName(file);
        var stamp = name.Length >= Prefix.Length + Stamp.Length
            ? name.Substring(Prefix.Length, Stamp.Length)
            : "";
        return DateTime.TryParseExact(stamp, Stamp, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)
            ? t
            : File.GetLastWriteTime(file);
    }
}
