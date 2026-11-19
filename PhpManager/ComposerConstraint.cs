using System.Text.RegularExpressions;

namespace PhpManager;

/// <summary>
/// Evaluates the Composer version constraints used for the PHP requirement
/// (<c>^8.2</c>, <c>~8.1.0</c>, <c>&gt;=8.1 &lt;8.4</c>, <c>8.2.*</c>, <c>^8.1 || ^8.2</c>).
/// </summary>
public static class ComposerConstraint
{
    private static readonly Regex Token = new(
        @"(?<op>\^|~>?|>=|<=|!=|<>|>|<|==|=)?\s*v?(?<ver>\d+(?:\.(?:\d+|\*|x))*(?:\.\*)?|\*|x)(?:-[\w.]+)?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static bool Satisfies(string? constraint, Version version)
    {
        if (string.IsNullOrWhiteSpace(constraint))
            return true;

        foreach (var orPart in Regex.Split(constraint, @"\s*\|\|?\s*"))
        {
            var text = Regex.Replace(orPart.Trim(), @"\s*,\s*", " ");
            // join operators separated from their version: ">= 8.1" -> ">=8.1"
            text = Regex.Replace(text, @"(\^|~>?|>=|<=|!=|<>|>|<|==|=)\s+", "$1");
            if (text.Length == 0)
                continue;

            var tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.All(t => TokenSatisfied(t, version)))
                return true;
        }

        return false;
    }

    private static bool TokenSatisfied(string token, Version v)
    {
        var m = Token.Match(token);
        if (!m.Success || m.Index != 0 || m.Length != token.Length && !token.Contains('-'))
            return false;

        var op = m.Groups["op"].Value;
        var raw = m.Groups["ver"].Value;
        if (raw is "*" or "x" or "X")
            return true;

        var parts = raw.Split('.');
        var nums = new int?[3];
        var wildcard = false;
        for (var i = 0; i < 3; i++)
        {
            if (i >= parts.Length) { nums[i] = null; continue; }
            if (parts[i] is "*" or "x" or "X") { wildcard = true; nums[i] = null; continue; }
            nums[i] = int.Parse(parts[i]);
        }

        var given = parts.Count(p => p is not ("*" or "x" or "X"));
        var low = new Version(nums[0] ?? 0, nums[1] ?? 0, nums[2] ?? 0);
        // The first component that was not given (or was a wildcard) defines the upper bound.
        Version Upper(int significant) => significant switch
        {
            1 => new Version((nums[0] ?? 0) + 1, 0, 0),
            2 => new Version(nums[0] ?? 0, (nums[1] ?? 0) + 1, 0),
            _ => new Version(nums[0] ?? 0, nums[1] ?? 0, (nums[2] ?? 0) + 1)
        };

        switch (op)
        {
            case "^":
                // caret: allow changes that don't touch the left-most non-zero component
                if ((nums[0] ?? 0) > 0 || given == 1)
                    return v >= low && v < Upper(1);
                if ((nums[1] ?? 0) > 0 || given == 2)
                    return v >= low && v < Upper(2);
                return v >= low && v < Upper(3);

            case "~" or "~>":
                // tilde: 8.2 -> <9.0, 8.2.1 -> <8.3
                return v >= low && v < Upper(Math.Max(1, given - 1));

            case ">=": return v >= low;
            case ">": return wildcard || given < 3 ? v >= Upper(given) : v > low;
            case "<=": return given < 3 ? v < Upper(given) : v <= low;
            case "<": return v < low;
            case "!=" or "<>": return given < 3 ? !(v >= low && v < Upper(given)) : v != low;

            default: // "", "=", "=="
                if (wildcard || given < 3)
                    return v >= low && v < Upper(Math.Max(1, given));
                return v == low;
        }
    }
}
