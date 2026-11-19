namespace PhpManager.Tests;

public sealed class ServyReleaseTests : IDisposable
{
    // The asset list of the real aelassas/servy v10.9 release.
    private static readonly string[] RealAssets =
    [
        "servy-10.9-arm64-installer.exe", "servy-10.9-arm64-portable.7z", "servy-10.9-net48-sbom.xml",
        "servy-10.9-net48-x64-installer.exe", "servy-10.9-net48-x64-portable.7z", "servy-10.9-sbom.xml",
        "servy-10.9-x64-installer.exe", "servy-10.9-x64-portable.7z"
    ];

    private readonly string _root = Path.Combine(Path.GetTempPath(), "php-manager-servy-" + Guid.NewGuid().ToString("N"));

    public ServyReleaseTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void DefaultUrl_IsTheAelassasReleasesPage()
    {
        Assert.Equal("https://github.com/aelassas/servy/releases", ServyRelease.DefaultReleasesUrl);
        Assert.True(ServyRelease.IsReleasesPage(ServyRelease.DefaultReleasesUrl));
    }

    [Theory]
    [InlineData("https://github.com/aelassas/servy/releases", true)]
    [InlineData("https://github.com/aelassas/servy/releases/", true)]
    [InlineData("https://github.com/aelassas/servy/releases/latest", true)]
    [InlineData("https://github.com/aelassas/servy/releases/tag/v10.9", true)]
    [InlineData("https://github.com/aelassas/servy/releases/download/v10.9/servy-10.9-net48-x64-portable.7z", false)]
    [InlineData("https://example.com/servy.zip", false)]
    [InlineData("https://github.com/aelassas/servy", false)]
    public void IsReleasesPage(string url, bool expected)
    {
        Assert.Equal(expected, ServyRelease.IsReleasesPage(url));
    }

    [Theory]
    [InlineData("https://github.com/aelassas/servy/releases", "https://api.github.com/repos/aelassas/servy/releases/latest")]
    [InlineData("https://github.com/aelassas/servy/releases/latest", "https://api.github.com/repos/aelassas/servy/releases/latest")]
    [InlineData("https://github.com/aelassas/servy/releases/tag/v10.9", "https://api.github.com/repos/aelassas/servy/releases/tags/v10.9")]
    public void ApiUrl_PointsAtTheRightRelease(string page, string expected)
    {
        Assert.Equal(expected, ServyRelease.ApiUrl(page));
    }

    [Fact]
    public void ApiUrl_ForADirectFileLink_IsNull()
    {
        Assert.Null(ServyRelease.ApiUrl("https://example.com/servy-cli.zip"));
    }

    [Fact]
    public void PickAsset_PrefersTheSmallNet48PortableBuild_FromTheRealRelease()
    {
        Assert.Equal("servy-10.9-net48-x64-portable.7z", ServyRelease.PickAsset(RealAssets));
    }

    [Fact]
    public void PickAsset_FallsBackToSelfContainedPortable_ButNeverInstallersArmOrSbom()
    {
        var withoutNet48 = RealAssets.Where(a => !a.Contains("net48")).ToArray();

        Assert.Equal("servy-10.9-x64-portable.7z", ServyRelease.PickAsset(withoutNet48));
        Assert.Null(ServyRelease.PickAsset(["servy-10.9-x64-installer.exe", "servy-10.9-arm64-portable.7z", "servy-10.9-sbom.xml"]));
    }

    [Fact]
    public void LegacyDefaults_MapTheOldWrongUrlsToTheCorrectOnes()
    {
        var download = ServyRelease.LegacyDefaults.Single(d => d.Key == "Download");
        var docs = ServyRelease.LegacyDefaults.Single(d => d.Category == "Documentation");

        Assert.Contains("nicholasgasior", download.OldUrl);
        Assert.Equal("https://github.com/aelassas/servy/releases", download.NewUrl);
        Assert.Equal("https://github.com/aelassas/servy", docs.NewUrl);
        Assert.All(ServyRelease.LegacyDefaults, d => Assert.DoesNotContain("nicholasgasior", d.NewUrl));
    }

    [Fact]
    public void InstallFrom_CopiesTheWholePackageFolder_NotJustTheExe()
    {
        var pkg = Directory.CreateDirectory(Path.Combine(_root, "extract", "servy-10.9-net48-x64-portable")).FullName;
        Directory.CreateDirectory(Path.Combine(pkg, "taskschd"));
        File.WriteAllText(Path.Combine(pkg, "servy-cli.exe"), "exe");
        File.WriteAllText(Path.Combine(pkg, "Servy.Core.dll"), "dll");
        File.WriteAllText(Path.Combine(pkg, "taskschd", "Write-ServyLog.ps1"), "ps1");
        var dest = Path.Combine(_root, "installed");

        var exe = ServyRelease.InstallFrom(Path.Combine(_root, "extract"), dest);

        Assert.Equal(Path.Combine(dest, "servy-cli.exe"), exe);
        Assert.True(File.Exists(Path.Combine(dest, "Servy.Core.dll")));
        Assert.True(File.Exists(Path.Combine(dest, "taskschd", "Write-ServyLog.ps1")));
    }

    [Fact]
    public void InstallFrom_WithoutTheCli_ThrowsAClearError()
    {
        Directory.CreateDirectory(Path.Combine(_root, "empty"));

        var ex = Assert.Throws<FileNotFoundException>(() => ServyRelease.InstallFrom(Path.Combine(_root, "empty"), Path.Combine(_root, "dest")));
        Assert.Contains("servy-cli.exe", ex.Message);
    }
}
