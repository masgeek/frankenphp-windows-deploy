namespace PhpManager.Tests;

public class LaravelExtensionsTests
{
    private const string Ini = "[PHP]\n;extension=curl\n;extension=mbstring\nextension=openssl\n;extension=zip\n[CLI Server]\nfoo=1\n";

    [Fact]
    public void Enable_UncommentsDisabledExtensions()
    {
        var result = LaravelExtensions.Enable(Ini, ["curl", "mbstring"]);

        Assert.Contains("\nextension=curl\n", result);
        Assert.Contains("\nextension=mbstring\n", result);
        Assert.DoesNotContain(";extension=curl", result);
        Assert.Contains(";extension=zip", result);
    }

    [Fact]
    public void Enable_LeavesAlreadyEnabledAlone_NoDuplicates()
    {
        var result = LaravelExtensions.Enable(Ini, ["openssl"]);

        Assert.Equal(Ini, result);
    }

    [Fact]
    public void Enable_AddsMissingExtension_BeforeNextSection()
    {
        var result = LaravelExtensions.Enable(Ini, ["gd"]);

        Assert.True(result.IndexOf("extension=gd", StringComparison.Ordinal) < result.IndexOf("[CLI Server]", StringComparison.Ordinal));
    }

    [Fact]
    public void Enable_HandlesPhpPrefixDllSuffixAndCrLf()
    {
        var ini = "[PHP]\r\n;extension=php_curl.dll\r\n;extension=zip\r\n";

        var result = LaravelExtensions.Enable(ini, ["curl", "zip"]);

        // the original spelling of the line (php_curl.dll) is kept; only the comment marker is removed
        Assert.Equal("[PHP]\r\nextension=php_curl.dll\r\nextension=zip\r\n", result);
    }

    [Fact]
    public void Enable_IsIdempotent()
    {
        var once = LaravelExtensions.Enable(Ini, LaravelExtensions.Names);

        Assert.Equal(once, LaravelExtensions.Enable(once, LaravelExtensions.Names));
    }

    [Fact]
    public void IsLaravel_IsCaseInsensitive()
    {
        Assert.True(LaravelExtensions.IsLaravel("MBSTRING"));
        Assert.False(LaravelExtensions.IsLaravel("xdebug"));
    }
}
