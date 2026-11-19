namespace PhpManager.Tests;

public sealed class InstallationCacheTests : IDisposable
{
    private readonly string _base = Path.Combine(Path.GetTempPath(), "php-manager-cache-" + Guid.NewGuid().ToString("N"));

    public InstallationCacheTests() => Directory.CreateDirectory(_base);

    public void Dispose() => Directory.Delete(_base, recursive: true);

    private string MakeInstall(string folder, string exeContent = "x")
    {
        var dir = Path.Combine(_base, folder);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "php.exe"), exeContent);
        return dir;
    }

    [Fact]
    public void ReadsVersion_FromAnyFolderName()
    {
        var dir = MakeInstall("latest");

        Assert.Equal("8.4.27", PhpInstallationCache.GetVersion(_base, dir, _ => "8.4.27"));
    }

    [Fact]
    public void WritesCacheFile_AndReusesIt_WithoutRunningPhp()
    {
        var dir = MakeInstall("php-stable");
        PhpInstallationCache.GetVersion(_base, dir, _ => "8.3.20");
        Assert.True(File.Exists(PhpInstallationCache.FilePath(_base)));

        var runs = 0;
        var again = PhpInstallationCache.GetVersion(_base, dir, _ => { runs++; return "9.9.9"; });

        Assert.Equal("8.3.20", again);
        Assert.Equal(0, runs);
    }

    [Fact]
    public void Rereads_WhenPhpExeChanges()
    {
        var dir = MakeInstall("current", "old");
        PhpInstallationCache.GetVersion(_base, dir, _ => "8.3.20");

        File.WriteAllText(Path.Combine(dir, "php.exe"), "a much longer replacement exe");

        Assert.Equal("8.4.1", PhpInstallationCache.GetVersion(_base, dir, _ => "8.4.1"));
    }

    [Fact]
    public void UnreadableVersion_ReturnsNull_AndIsNotCached()
    {
        var dir = MakeInstall("broken");

        Assert.Null(PhpInstallationCache.GetVersion(_base, dir, _ => throw new InvalidOperationException()));
        Assert.Null(PhpInstallationCache.GetVersion(_base, dir, _ => "no version here"));
        Assert.False(File.Exists(PhpInstallationCache.FilePath(_base)));
    }

    [Fact]
    public void MissingPhpExe_ReturnsNull()
    {
        var dir = Path.Combine(_base, "empty");
        Directory.CreateDirectory(dir);

        Assert.Null(PhpInstallationCache.GetVersion(_base, dir, _ => "8.4.1"));
    }

    [Fact]
    public void CorruptCacheFile_IsRebuilt()
    {
        File.WriteAllText(PhpInstallationCache.FilePath(_base), "{ not json");
        var dir = MakeInstall("x");

        Assert.Equal("8.2.0", PhpInstallationCache.GetVersion(_base, dir, _ => "8.2.0"));
    }

    [Fact]
    public void GetVersions_ResolvesManyFolders()
    {
        var a = MakeInstall("a");
        var b = MakeInstall("b");

        var result = PhpInstallationCache.GetVersions(_base, [a, b],
            exe => Path.GetFileName(Path.GetDirectoryName(exe)) == "a" ? "8.1.1" : "8.2.2");

        Assert.Equal("8.1.1", result[a]);
        Assert.Equal("8.2.2", result[b]);
    }
}
