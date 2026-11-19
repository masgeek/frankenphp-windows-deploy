using System.Text.Json;

namespace PhpManager;

/// <summary>A rectangle in physical screen pixels (virtual-screen coordinates; can be negative on multi-monitor setups).</summary>
public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;

    public long OverlapArea(PixelRect other)
    {
        var w = Math.Min(Right, other.Right) - Math.Max(Left, other.Left);
        var h = Math.Min(Bottom, other.Bottom) - Math.Max(Top, other.Top);
        return w > 0 && h > 0 ? (long)w * h : 0;
    }
}

/// <summary>What is remembered about the main window between runs.</summary>
public sealed class SavedWindowPlacement
{
    public int Left { get; set; }
    public int Top { get; set; }
    public int Right { get; set; }
    public int Bottom { get; set; }
    public bool Maximized { get; set; }

    /// <summary>Device name of the monitor the window was on (for information; the rectangle is what is restored).</summary>
    public string Monitor { get; set; } = "";

    public PixelRect Rect => new(Left, Top, Right, Bottom);
}

/// <summary>
/// Pure rules for restoring a window: never put it somewhere the user cannot see it (a monitor that has been
/// unplugged, a resolution change) and never let it be larger than the monitor it lands on.
/// </summary>
public static class WindowPlacementRules
{
    public const int MinWidth = 640;
    public const int MinHeight = 420;

    /// <summary>At least this much of the window must be on a monitor for the saved position to count as visible.</summary>
    private const int MinVisibleWidth = 120;
    private const int MinVisibleHeight = 60;

    /// <summary>
    /// The rectangle to restore to, or null when the saved one is unusable (nonsense size, or not on any connected
    /// monitor), in which case the caller should fall back to the default centred position.
    /// </summary>
    public static PixelRect? Fit(PixelRect saved, IReadOnlyList<PixelRect> workAreas)
    {
        if (saved.Width < 200 || saved.Height < 150 || saved.Width > 20000 || saved.Height > 20000 || workAreas.Count == 0)
            return null;

        // the monitor holding the biggest part of the window
        var best = workAreas
            .Select(area => (Area: area, Overlap: saved.OverlapArea(area)))
            .OrderByDescending(x => x.Overlap)
            .First();

        var overlapW = Math.Min(saved.Right, best.Area.Right) - Math.Max(saved.Left, best.Area.Left);
        var overlapH = Math.Min(saved.Bottom, best.Area.Bottom) - Math.Max(saved.Top, best.Area.Top);
        if (overlapW < MinVisibleWidth || overlapH < MinVisibleHeight)
            return null;

        // shrink to the monitor if needed, then shift fully inside it
        var width = Math.Clamp(saved.Width, Math.Min(MinWidth, best.Area.Width), best.Area.Width);
        var height = Math.Clamp(saved.Height, Math.Min(MinHeight, best.Area.Height), best.Area.Height);
        var left = Math.Clamp(saved.Left, best.Area.Left, best.Area.Right - width);
        var top = Math.Clamp(saved.Top, best.Area.Top, best.Area.Bottom - height);

        return new PixelRect(left, top, left + width, top + height);
    }

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string Serialize(SavedWindowPlacement placement) => JsonSerializer.Serialize(placement, Json);

    /// <summary>Null for missing, corrupt or empty data (the window then opens at its default position).</summary>
    public static SavedWindowPlacement? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            var placement = JsonSerializer.Deserialize<SavedWindowPlacement>(json);
            return placement != null && placement.Rect.Width > 0 && placement.Rect.Height > 0 ? placement : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
