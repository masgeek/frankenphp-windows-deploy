namespace PhpManager.Tests;

public sealed class PathDetectionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "php-manager-tests-" + Guid.NewGuid().ToString("N"));

    public PathDetectionTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string MakeDir(string name, bool withPhp)
    {
        var dir = Path.Combine(_root, name);
        Directory.CreateDirectory(dir);
        if (withPhp)
            File.WriteAllText(Path.Combine(dir, "php.exe"), "");
        return dir;
    }

    [Fact]
    public void EnumerateDirs_SplitsTrimsUnquotesAndDedupes()
    {
        var dirs = PhpPathDetector.EnumerateDirs(@"C:\a; ""C:\b"" ;;C:\A", null, @"C:\c").ToList();

        Assert.Equal([@"C:\a", @"C:\b", @"C:\c"], dirs);
    }

    [Fact]
    public void EnumerateDirs_ExpandsEnvironmentVariables()
    {
        Environment.SetEnvironmentVariable("PHPMGR_TEST_DIR", @"C:\expanded");
        try
        {
            Assert.Equal([@"C:\expanded\bin"], PhpPathDetector.EnumerateDirs(@"%PHPMGR_TEST_DIR%\bin"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PHPMGR_TEST_DIR", null);
        }
    }

    [Fact]
    public void Find_ReturnsNull_WhenNoPhpExe()
    {
        Assert.Null(PhpPathDetector.Find([MakeDir("empty", false)], _ => "8.4.1"));
    }

    [Fact]
    public void Find_ReturnsFirstMatch_WithParsedVersion()
    {
        var first = MakeDir("first", true);
        var second = MakeDir("second", true);

        var result = PhpPathDetector.Find([MakeDir("none", false), first, second], _ => "8.3.20");

        Assert.NotNull(result);
        Assert.Equal("8.3.20", result.Version);
        Assert.Equal(first, result.InstallDir);
        Assert.True(result.IsExternal);
        Assert.True(result.Installed);
    }

    [Fact]
    public void Find_ExtractsVersionFromNoisyOutput()
    {
        var result = PhpPathDetector.Find([MakeDir("p", true)], _ => "Warning: something\n8.2.5\n");

        Assert.Equal("8.2.5", result!.Version);
    }

    [Fact]
    public void Find_FallsBackToGenericLabel_WhenVersionUnparseable()
    {
        var result = PhpPathDetector.Find([MakeDir("p", true)], _ => "garbage");

        Assert.Equal("PHP", result!.Version);
    }

    [Fact]
    public void Find_SkipsEntries_WhereRunningPhpFails()
    {
        var broken = MakeDir("broken", true);
        var good = MakeDir("good", true);

        var result = PhpPathDetector.Find([broken, good], exe =>
            exe.Contains("broken") ? throw new InvalidOperationException("cannot start") : "8.1.0");

        Assert.Equal(good, result!.InstallDir);
    }

    [Fact]
    public void Find_SkipsEntries_WhereVersionReadReturnsNull()
    {
        var timedOut = MakeDir("timedout", true);
        var good = MakeDir("good", true);

        var result = PhpPathDetector.Find([timedOut, good], exe => exe.Contains("timedout") ? null : "8.1.0");

        Assert.Equal(good, result!.InstallDir);
    }

    [Theory]
    [InlineData(@"C:\PHP\8.4.1", @"C:\PHP", true)]
    [InlineData(@"C:\PHP\8.4.1\", @"C:\PHP\", true)]
    [InlineData(@"c:\php\frankenphp", @"C:\PHP", true)]
    [InlineData(@"C:\PHP\latest", @"C:\PHP", true)]
    [InlineData(@"C:\PHP\php-8.4", @"C:\PHP", true)]
    [InlineData(@"C:\PHP", @"C:\PHP", false)]
    [InlineData(@"C:\PHP\8.4.1\bin", @"C:\PHP", false)]
    [InlineData(@"C:\PHPX\8.4.1", @"C:\PHP", false)]
    [InlineData(@"C:\tools\php", @"C:\PHP", false)]
    public void IsManaged_OnlyDirectChildrenOfBasePath(string dir, string basePath, bool expected)
    {
        Assert.Equal(expected, PhpPathDetector.IsManaged(dir, basePath));
    }
}
