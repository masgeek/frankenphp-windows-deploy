namespace PhpManager.Tests;

public class WindowPlacementTests
{
    private static readonly PixelRect Primary = new(0, 0, 1920, 1040);          // work area of the primary monitor
    private static readonly PixelRect Right = new(1920, 0, 3840, 1040);          // second monitor to the right
    private static readonly PixelRect Left = new(-1920, 0, 0, 1040);             // second monitor to the left (negative coordinates)

    [Fact]
    public void WindowFullyOnAMonitor_IsRestoredExactly()
    {
        var saved = new PixelRect(100, 80, 1000, 780);

        Assert.Equal(saved, WindowPlacementRules.Fit(saved, [Primary]));
    }

    [Fact]
    public void WindowOnTheSecondMonitor_StaysThere()
    {
        var saved = new PixelRect(2100, 100, 3000, 800);

        Assert.Equal(saved, WindowPlacementRules.Fit(saved, [Primary, Right]));
    }

    [Fact]
    public void WindowOnAMonitorLeftOfPrimary_NegativeCoordinatesAreKept()
    {
        var saved = new PixelRect(-1700, 100, -800, 800);

        Assert.Equal(saved, WindowPlacementRules.Fit(saved, [Primary, Left]));
    }

    [Fact]
    public void SecondMonitorUnplugged_FallsBackToTheDefault()
    {
        var saved = new PixelRect(2100, 100, 3000, 800);

        Assert.Null(WindowPlacementRules.Fit(saved, [Primary]));
    }

    [Fact]
    public void WindowPartlyOffScreen_IsShiftedFullyInside()
    {
        var saved = new PixelRect(1500, 600, 2400, 1300); // hangs off the right and bottom edges of the primary monitor

        var fit = WindowPlacementRules.Fit(saved, [Primary])!.Value;

        Assert.True(fit.Right <= Primary.Right && fit.Bottom <= Primary.Bottom);
        Assert.Equal(900, fit.Width);
        Assert.Equal(700, fit.Height);
    }

    [Fact]
    public void WindowLargerThanTheMonitor_IsShrunkToFit()
    {
        var saved = new PixelRect(0, 0, 3000, 2000);

        var fit = WindowPlacementRules.Fit(saved, [Primary])!.Value;

        Assert.Equal(Primary, fit);
    }

    [Fact]
    public void WindowSpanningTwoMonitors_GoesToTheOneHoldingMostOfIt()
    {
        var saved = new PixelRect(1500, 100, 2900, 800); // 420px on primary, 980px on the right monitor

        var fit = WindowPlacementRules.Fit(saved, [Primary, Right])!.Value;

        Assert.True(fit.Left >= Right.Left && fit.Right <= Right.Right);
    }

    [Fact]
    public void BarelyVisibleWindow_CountsAsOffScreen()
    {
        var saved = new PixelRect(1900, 100, 2800, 800); // only 20px on the primary monitor

        Assert.Null(WindowPlacementRules.Fit(saved, [Primary]));
    }

    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(0, 0, 50, 50)]
    [InlineData(0, 0, 100000, 100000)]
    [InlineData(500, 500, 100, 100)]
    public void NonsenseSizes_AreRejected(int l, int t, int r, int b)
    {
        Assert.Null(WindowPlacementRules.Fit(new PixelRect(l, t, r, b), [Primary]));
    }

    [Fact]
    public void NoMonitors_IsRejected()
    {
        Assert.Null(WindowPlacementRules.Fit(new PixelRect(0, 0, 900, 700), []));
    }

    [Fact]
    public void SerializeDeserialize_RoundTrips_IncludingMaximizedAndMonitor()
    {
        var saved = new SavedWindowPlacement { Left = -1700, Top = 40, Right = -800, Bottom = 740, Maximized = true, Monitor = @"\\.\DISPLAY2" };

        var copy = WindowPlacementRules.Deserialize(WindowPlacementRules.Serialize(saved))!;

        Assert.Equal(saved.Rect, copy.Rect);
        Assert.True(copy.Maximized);
        Assert.Equal(@"\\.\DISPLAY2", copy.Monitor);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("{\"Left\":0,\"Top\":0,\"Right\":0,\"Bottom\":0}")]
    public void MissingOrCorruptData_IsIgnored(string? json)
    {
        Assert.Null(WindowPlacementRules.Deserialize(json));
    }

    [Fact]
    public void OverlapArea_IsZeroForDisjointRects()
    {
        Assert.Equal(0, new PixelRect(0, 0, 10, 10).OverlapArea(new PixelRect(20, 20, 30, 30)));
        Assert.Equal(25, new PixelRect(0, 0, 10, 10).OverlapArea(new PixelRect(5, 5, 30, 30)));
    }
}
