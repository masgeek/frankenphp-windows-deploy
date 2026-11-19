namespace PhpManager.Tests;

public class ZendExtensionTests
{
    // Real output from `php --version` with zend_extension=xdebug enabled but php_xdebug.dll missing.
    private const string RealOutput =
        "PHP Warning:  Failed loading Zend extension 'xdebug' (tried: C:\\PHP\\8.4.27\\ext\\xdebug (The specified module could not be found), C:\\PHP\\8.4.27\\ext\\php_xdebug.dll (The specified module could not be found)) in Unknown on line 0\r\n" +
        "\r\n" +
        "Warning: Failed loading Zend extension 'xdebug' (tried: C:\\PHP\\8.4.27\\ext\\xdebug (The specified module could not be found), C:\\PHP\\8.4.27\\ext\\php_xdebug.dll (The specified module could not be found)) in Unknown on line 0\r\n" +
        "PHP 8.4.27 (cli) (built: Oct 20 2026 04:19:45) (NTS Visual C++ 2022 x64)\r\n";

    private static IReadOnlySet<string> Dlls(params string[] names) =>
        new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void Parser_ExplainsTheRealXdebugFailure()
    {
        var result = PhpStartupWarnings.ParseLoadFailures(RealOutput);

        var reason = Assert.Single(result).Value;
        Assert.Contains("The specified module could not be found", reason);
        Assert.Contains("php_xdebug.dll", reason);
        Assert.True(result.ContainsKey("xdebug"));
    }

    [Fact]
    public void Parser_StillReadsTheVersionLine_AfterTheWarnings()
    {
        Assert.Equal((8, 4, 27), PhpVersionParser.Parse(RealOutput));
    }

    [Fact]
    public void EnabledZendExtension_WithNoDll_IsReportedAsMissing()
    {
        var ini = "[PHP]\nzend_extension=xdebug\nextension=curl\n";

        var result = ExtensionHealth.Classify(ini, Dlls("curl"), ["curl"], PhpStartupWarnings.ParseLoadFailures(RealOutput), false);

        Assert.Equal(["xdebug"], result.NoDll);
        Assert.Equal(["curl"], result.Loaded);
        Assert.Contains("php_xdebug.dll is not in the ext folder", result.Reasons["xdebug"]);
    }

    [Fact]
    public void ZendExtension_WithDllButPhpFailedToLoadIt_IsFailedWithPhpsReason()
    {
        var ini = "zend_extension=xdebug\n";
        var failures = PhpStartupWarnings.ParseLoadFailures(RealOutput);

        var result = ExtensionHealth.Classify(ini, Dlls("xdebug"), [], failures, false);

        Assert.Equal(["xdebug"], result.Failed);
        Assert.Contains("could not be found", result.Reasons["xdebug"]);
    }

    [Fact]
    public void ZendExtension_WithDllAndNoWarning_IsLoaded()
    {
        var result = ExtensionHealth.Classify("zend_extension=opcache\n", Dlls("opcache"), [], new Dictionary<string, string>(), false);

        Assert.Equal(["opcache"], result.Loaded);
        Assert.Empty(result.Failed);
        Assert.Empty(result.NoDll);
    }

    [Fact]
    public void CommentedZendExtension_IsIgnored()
    {
        var result = ExtensionHealth.Classify(";zend_extension=xdebug\n", Dlls(), [], new Dictionary<string, string>(), false);

        Assert.Empty(result.NoDll);
        Assert.Empty(result.Loaded);
    }

    [Fact]
    public void ZendExtensionGivenAsFullPath_UsesThatFile()
    {
        var ini = "zend_extension=\"D:\\tools\\php_xdebug.dll\"\n";

        var present = ExtensionHealth.Classify(ini, Dlls(), [], new Dictionary<string, string>(), false, f => f == "D:\\tools\\php_xdebug.dll");
        var absent = ExtensionHealth.Classify(ini, Dlls(), [], new Dictionary<string, string>(), false, _ => false);

        Assert.Equal(["xdebug"], present.Loaded);
        Assert.Equal(["xdebug"], absent.NoDll);
    }

    [Fact]
    public void PlainExtensions_BehaveAsBefore()
    {
        var ini = "extension=curl\nextension=intl\nextension=gone\n";
        var failures = new Dictionary<string, string> { ["intl"] = "The specified module could not be found" };

        var result = ExtensionHealth.Classify(ini, Dlls("curl", "intl"), ["curl"], failures, false);

        Assert.Equal(["curl"], result.Loaded);
        Assert.Equal(["intl"], result.Failed);
        Assert.Equal(["gone"], result.NoDll);
    }

    [Fact]
    public void DisableExtensions_AlsoSwitchesOffZendLines()
    {
        var ini = "[PHP]\nzend_extension=xdebug\nzend_extension=opcache\nextension=curl\n";

        var result = PhpIniEditor.DisableExtensions(ini, ["xdebug"]);

        Assert.Equal("[PHP]\n;zend_extension=xdebug\nzend_extension=opcache\nextension=curl\n", result);
    }

    [Fact]
    public void FixingTheRealCase_LeavesNoActiveBrokenLine()
    {
        var ini = "[PHP]\nzend_extension=xdebug\nextension=curl\n";
        var before = ExtensionHealth.Classify(ini, Dlls("curl"), ["curl"], PhpStartupWarnings.ParseLoadFailures(RealOutput), false);
        var broken = before.Failed.Concat(before.NoDll).ToList();

        var fixedIni = PhpIniEditor.DisableExtensions(ini, broken);
        var after = ExtensionHealth.Classify(fixedIni, Dlls("curl"), ["curl"], new Dictionary<string, string>(), false);

        Assert.Equal(["xdebug"], broken);
        Assert.Empty(after.Failed);
        Assert.Empty(after.NoDll);
        Assert.Contains(";zend_extension=xdebug", fixedIni);
    }
}
