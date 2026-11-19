namespace PhpManager.Tests;

public sealed class IniTemplateTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "php-manager-ini-" + Guid.NewGuid().ToString("N"));
    private readonly string _install;
    private readonly string _app;

    public IniTemplateTests()
    {
        _install = Directory.CreateDirectory(Path.Combine(_root, "install")).FullName;
        _app = Directory.CreateDirectory(Path.Combine(_root, "app")).FullName;
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Theory]
    [InlineData(IniProfile.Development, "php.ini-development")]
    [InlineData(IniProfile.Production, "php.ini-production")]
    public void FileName_MatchesPhpDistributionNames(IniProfile profile, string expected)
    {
        Assert.Equal(expected, PhpIniTemplates.FileName(profile));
    }

    [Fact]
    public void Resolve_PrefersInstallFolder_OverAppFolder()
    {
        File.WriteAllText(Path.Combine(_install, "php.ini-production"), "");
        File.WriteAllText(Path.Combine(_app, "php.ini-production"), "");

        Assert.Equal(Path.Combine(_install, "php.ini-production"),
            PhpIniTemplates.Resolve(_install, _app, IniProfile.Production));
    }

    [Fact]
    public void Resolve_FallsBackToAppFolder()
    {
        File.WriteAllText(Path.Combine(_app, "php.ini-development"), "");

        Assert.Equal(Path.Combine(_app, "php.ini-development"),
            PhpIniTemplates.Resolve(_install, _app, IniProfile.Development));
    }

    [Fact]
    public void Resolve_ReturnsNull_WhenMissing_AndDoesNotMixProfiles()
    {
        File.WriteAllText(Path.Combine(_install, "php.ini-development"), "");

        Assert.Null(PhpIniTemplates.Resolve(_install, _app, IniProfile.Production));
    }
}
