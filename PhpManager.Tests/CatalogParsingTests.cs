using System.Text.RegularExpressions;

namespace PhpManager.Tests;

public class CatalogParsingTests
{
    private const string PrimaryTemplate =
        "https://windows.php.net/downloads/releases/php-{0}-nts-Win32-vs17-x64.zip";

    private static string Pattern => Urls.PhpCatalog.BuildPatternFromTemplate(PrimaryTemplate, @"(\d+\.\d+\.\d+)");

    [Fact]
    public void Pattern_MatchesFilenameOnly_NotDirectory()
    {
        Assert.DoesNotContain("downloads", Pattern);
        Assert.Contains("php-", Pattern);
    }

    [Fact]
    public void Pattern_CapturesVersion()
    {
        var m = Regex.Match("php-8.4.11-nts-Win32-vs17-x64.zip", Pattern);
        Assert.True(m.Success);
        Assert.Equal("8.4.11", m.Groups[1].Value);
    }

    [Fact]
    public void ParseVersions_FindsVersionsAcrossDifferentMirrorDirectories()
    {
        const string html = """
            <a href="/downloads/releases/php-8.4.11-nts-Win32-vs17-x64.zip">a</a>
            <a href="/~windows/releases/php-8.3.20-nts-Win32-vs17-x64.zip">b</a>
            <a href="/downloads/releases/archives/php-7.4.33-nts-Win32-vc15-x64.zip">older compiler</a>
            <a href="/downloads/releases/php-8.2.1-nts-Win32-vs16-x64.zip">vs16</a>
            <a href="/downloads/releases/php-7.4.33-nts-Win32-vc15-x86.zip">wrong arch</a>
            """;

        var versions = Urls.PhpCatalog.ParseVersions(html, Pattern).ToList();

        Assert.Equal(["8.4.11", "8.3.20", "7.4.33", "8.2.1"], versions);
    }

    [Fact]
    public void ParseVersions_IgnoresTsAndDebugPackages()
    {
        const string html = """
            php-8.4.11-Win32-vs17-x64.zip
            php-debug-pack-8.4.11-nts-Win32-vs17-x64.zip
            """;

        Assert.Empty(Urls.PhpCatalog.ParseVersions(html, Pattern));
    }

    [Fact]
    public void ParseVersions_DeduplicatesRepeatedLinks()
    {
        const string html = "php-8.4.1-nts-Win32-vs17-x64.zip php-8.4.1-nts-Win32-vs17-x64.zip";

        Assert.Single(Urls.PhpCatalog.ParseVersions(html, Pattern));
    }

    [Fact]
    public void ParseVersions_IsCaseInsensitive()
    {
        Assert.Single(Urls.PhpCatalog.ParseVersions("PHP-8.4.1-NTS-WIN32-VS17-X64.ZIP", Pattern));
    }

    [Fact]
    public void Pattern_FollowsEditedTemplate()
    {
        var pattern = Urls.PhpCatalog.BuildPatternFromTemplate(
            "https://example.com/x/php-{0}-ts-Win32-vs17-x86.zip", @"(\d+\.\d+\.\d+)");

        Assert.Single(Urls.PhpCatalog.ParseVersions("php-8.5.0-ts-Win32-vs17-x86.zip", pattern));
        Assert.Empty(Urls.PhpCatalog.ParseVersions("php-8.5.0-nts-Win32-vs17-x64.zip", pattern));
    }

    [Fact]
    public void ExpandCompilers_ReturnsTemplateFirst_ThenEachCompiler()
    {
        var urls = Urls.PhpDownload.ExpandCompilers(PrimaryTemplate).ToList();

        Assert.Equal(PrimaryTemplate, urls[0]);
        Assert.Contains(urls, u => u.Contains("-vs16-"));
        Assert.Contains(urls, u => u.Contains("-vc15-"));
        Assert.Contains(urls, u => u.Contains("-vc14-"));
        Assert.All(urls, u => Assert.Contains("{0}", u));
    }

    [Fact]
    public void ExpandCompilers_TemplateWithoutCompiler_IsReturnedAlone()
    {
        Assert.Equal(["https://x/php-{0}.zip"], Urls.PhpDownload.ExpandCompilers("https://x/php-{0}.zip"));
    }
}
