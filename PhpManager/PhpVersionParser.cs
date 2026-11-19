using System.Text.RegularExpressions;

namespace PhpManager;

/// <summary>Parses <c>php --version</c> output. Startup warnings can precede the version line, so it isn't anchored to the start.</summary>
public static class PhpVersionParser
{
    private static readonly Regex VersionLine =
        new(@"(?m)^[\s﻿]*PHP (\d+)\.(\d+)\.(\d+)", RegexOptions.Compiled);

    public static (int Major, int Minor, int Patch)? Parse(string? output)
    {
        var m = VersionLine.Match(output ?? "");
        return m.Success
            ? (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value))
            : null;
    }
}
