namespace PhpManager;

public enum DiffKind
{
    Same,
    Added,
    Removed,
    /// <summary>Marks skipped unchanged lines between hunks.</summary>
    Gap
}

public sealed record DiffLine(DiffKind Kind, string Text)
{
    public string Display => Kind switch
    {
        DiffKind.Added => "+ " + Text,
        DiffKind.Removed => "- " + Text,
        DiffKind.Gap => "  ...",
        _ => "  " + Text
    };
}

/// <summary>Line-based diff (longest common subsequence) used to compare php.ini backups with the current file.</summary>
public static class LineDiff
{
    public static List<DiffLine> Compute(string oldText, string newText)
    {
        var a = Split(oldText);
        var b = Split(newText);

        // Trim the common prefix and suffix so the table only covers what actually differs.
        var start = 0;
        while (start < a.Length && start < b.Length && a[start] == b[start])
            start++;

        var endA = a.Length;
        var endB = b.Length;
        while (endA > start && endB > start && a[endA - 1] == b[endB - 1])
        {
            endA--;
            endB--;
        }

        var result = new List<DiffLine>();
        for (var i = 0; i < start; i++)
            result.Add(new DiffLine(DiffKind.Same, a[i]));

        var n = endA - start;
        var m = endB - start;
        var table = new int[n + 1, m + 1];
        for (var i = n - 1; i >= 0; i--)
            for (var j = m - 1; j >= 0; j--)
                table[i, j] = a[start + i] == b[start + j]
                    ? table[i + 1, j + 1] + 1
                    : Math.Max(table[i + 1, j], table[i, j + 1]);

        int x = 0, y = 0;
        while (x < n && y < m)
        {
            if (a[start + x] == b[start + y])
            {
                result.Add(new DiffLine(DiffKind.Same, a[start + x]));
                x++;
                y++;
            }
            else if (table[x + 1, y] >= table[x, y + 1])
            {
                result.Add(new DiffLine(DiffKind.Removed, a[start + x++]));
            }
            else
            {
                result.Add(new DiffLine(DiffKind.Added, b[start + y++]));
            }
        }

        while (x < n) result.Add(new DiffLine(DiffKind.Removed, a[start + x++]));
        while (y < m) result.Add(new DiffLine(DiffKind.Added, b[start + y++]));

        for (var i = endA; i < a.Length; i++)
            result.Add(new DiffLine(DiffKind.Same, a[i]));

        return result;
    }

    /// <summary>Only the changed lines plus <paramref name="context"/> lines around them; skipped stretches become one Gap line.</summary>
    public static List<DiffLine> WithContext(IReadOnlyList<DiffLine> lines, int context = 2)
    {
        var keep = new bool[lines.Count];
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].Kind == DiffKind.Same)
                continue;
            for (var k = Math.Max(0, i - context); k <= Math.Min(lines.Count - 1, i + context); k++)
                keep[k] = true;
        }

        var result = new List<DiffLine>();
        var skipping = false;
        for (var i = 0; i < lines.Count; i++)
        {
            if (keep[i])
            {
                result.Add(lines[i]);
                skipping = false;
            }
            else if (!skipping)
            {
                result.Add(new DiffLine(DiffKind.Gap, ""));
                skipping = true;
            }
        }

        // a leading/trailing gap just means "unchanged", which is noise
        if (result.Count > 0 && result[0].Kind == DiffKind.Gap) result.RemoveAt(0);
        if (result.Count > 0 && result[^1].Kind == DiffKind.Gap) result.RemoveAt(result.Count - 1);
        return result;
    }

    public static bool HasChanges(IEnumerable<DiffLine> lines) => lines.Any(l => l.Kind is DiffKind.Added or DiffKind.Removed);

    private static string[] Split(string text) =>
        text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
}
