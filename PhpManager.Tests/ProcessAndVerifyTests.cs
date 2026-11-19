using System.Diagnostics;

namespace PhpManager.Tests;

public class ProcessRunnerTests
{
    private static readonly string Cmd = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";

    [Fact]
    public void CapturesStdoutStderrAndExitCode()
    {
        var r = ProcessRunner.Run(Cmd, "/c \"echo out& echo err 1>&2& exit /b 3\"");

        Assert.Contains("out", r.Output);
        Assert.Contains("err", r.Error);
        Assert.Equal(3, r.ExitCode);
        Assert.False(r.TimedOut);
    }

    [Fact]
    public void LargeStderr_DoesNotDeadlock()
    {
        // ~400 KB to stderr overflows the pipe buffer if stderr isn't drained while the process runs.
        var script = "for /l %i in (1,1,4000) do @echo 0123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789 1>&2";
        var sw = Stopwatch.StartNew();

        var r = ProcessRunner.Run(Cmd, $"/c \"{script}\"", timeoutMs: 30_000);

        Assert.False(r.TimedOut);
        Assert.True(r.Error.Length > 400_000);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(25));
    }

    [Fact]
    public void HungProcess_IsKilledAfterTimeout()
    {
        var sw = Stopwatch.StartNew();

        var r = ProcessRunner.Run(Cmd, "/c \"ping -n 30 127.0.0.1 >nul\"", timeoutMs: 1000);

        Assert.True(r.TimedOut);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void ProgramWaitingOnStdin_DoesNotHang()
    {
        var r = ProcessRunner.Run(Cmd, "/c \"set /p x=\"", timeoutMs: 10_000);

        Assert.False(r.TimedOut);
    }

    [Fact]
    public void MissingExecutable_ReturnsFailure_NotException()
    {
        var r = ProcessRunner.Run(Path.Combine(Path.GetTempPath(), "definitely-missing-php.exe"), "-v");

        Assert.Equal(1, r.ExitCode);
        Assert.NotEmpty(r.Error);
    }

    [Fact]
    public void PassesEnvironmentVariables()
    {
        var r = ProcessRunner.Run(Cmd, "/c \"echo %PHPMGR_X%\"", env: new Dictionary<string, string> { ["PHPMGR_X"] = "hello" });

        Assert.Contains("hello", r.Output);
    }
}

public class PhpStartupWarningsTests
{
    [Fact]
    public void ParsesLoadFailures_WithReason()
    {
        const string output =
            "Warning: PHP Startup: Unable to load dynamic library 'php_intl.dll' (tried: ext/php_intl.dll (The specified module could not be found), ext/php_php_intl.dll.dll (The specified module could not be found)) in Unknown on line 0\r\n" +
            "PHP Warning:  PHP Startup: Unable to load dynamic library 'gd' (tried: ext/gd (not found)) in Unknown on line 0\r\n";

        var result = PhpStartupWarnings.ParseLoadFailures(output);

        Assert.Equal("The specified module could not be found", result["intl"]);
        Assert.True(result.ContainsKey("gd"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("curl\nmbstring\n")]
    public void NoWarnings_ReturnsEmpty(string? output)
    {
        Assert.Empty(PhpStartupWarnings.ParseLoadFailures(output));
    }
}

public class PhpIniEditorTests
{
    [Fact]
    public void DisableExtensions_CommentsOutOnlyNamedActiveLines()
    {
        const string ini = "[PHP]\nextension=curl\nextension=php_intl.dll\n;extension=zip\nextension=mbstring\n";

        var result = PhpIniEditor.DisableExtensions(ini, ["intl", "zip", "missing"]);

        Assert.Equal("[PHP]\nextension=curl\n;extension=php_intl.dll\n;extension=zip\nextension=mbstring\n", result);
    }

    [Fact]
    public void DisableExtensions_PreservesCrLf_AndDoesNotMatchPrefixes()
    {
        const string ini = "extension=pdo\r\nextension=pdo_mysql\r\n";

        var result = PhpIniEditor.DisableExtensions(ini, ["pdo"]);

        Assert.Equal(";extension=pdo\r\nextension=pdo_mysql\r\n", result);
    }
}
