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

public class PhpStartupWarningExplanationTests
{
    [Fact]
    public void AlreadyLoaded_IsExplainedAsADuplicateLine()
    {
        var result = PhpStartupWarnings.ParseLoadFailures("Warning: Module \"pdo_sqlsrv\" is already loaded in Unknown on line 0\r\n");

        Assert.Contains("more than once", result["pdo_sqlsrv"]);
    }

    [Fact]
    public void ModuleApiMismatch_IsExplainedAsWrongPhpVersion()
    {
        const string output =
            "PHP Warning:  PHP Startup: pdo_sqlsrv: Unable to initialize module\r\n" +
            "Module compiled with module API=20220829\r\n" +
            "PHP    compiled with module API=20230831\r\n" +
            "These options need to match\r\n in Unknown on line 0\r\n";

        var reason = PhpStartupWarnings.ParseLoadFailures(output)["pdo_sqlsrv"];

        Assert.Contains("20220829", reason);
        Assert.Contains("20230831", reason);
        Assert.Contains("different PHP version", reason);
    }

    [Fact]
    public void BuildIdMismatch_ExplainsThreadSafety()
    {
        const string output =
            "Warning: PHP Startup: sqlsrv: Unable to initialize module\r\n" +
            "Module compiled with build ID=API20230831,NTS,VS16\r\n" +
            "PHP    compiled with build ID=API20230831,TS,VS16\r\n" +
            "These options need to match\r\n";

        var reason = PhpStartupWarnings.ParseLoadFailures(output)["sqlsrv"];

        Assert.Contains("NTS", reason);
        Assert.Contains("TS", reason);
        Assert.Contains("thread-safe", reason);
    }

    [Fact]
    public void SeveralProblems_AreAllReported()
    {
        const string output =
            "Warning: Module \"curl\" is already loaded in Unknown on line 0\r\n" +
            "Warning: PHP Startup: Unable to load dynamic library 'intl' (tried: x (The specified module could not be found)) in Unknown on line 0\r\n";

        var result = PhpStartupWarnings.ParseLoadFailures(output);

        Assert.Equal(2, result.Count);
        Assert.Equal("The specified module could not be found", result["intl"]);
    }
}
