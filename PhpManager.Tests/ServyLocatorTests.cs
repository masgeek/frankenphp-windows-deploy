namespace PhpManager.Tests;

public class ServyLocatorTests
{
    private static Func<string, bool> Exists(params string[] files) =>
        f => files.Contains(f, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void FindsServyOnPath_ByFile_WithoutRunningIt()
    {
        var found = ServyLocator.Find([], [@"C:\Windows", @"D:\tools\servy"], [], Exists(@"D:\tools\servy\servy-cli.exe"));

        Assert.Equal(@"D:\tools\servy\servy-cli.exe", found);
    }

    [Fact]
    public void PrefersTheCopyPhpManagerInstalled_ThenPath_ThenKnownFolders()
    {
        var all = Exists(@"C:\own\servy-cli.exe", @"D:\path\servy-cli.exe", @"C:\Program Files\Servy\servy-cli.exe");

        Assert.Equal(@"C:\own\servy-cli.exe", ServyLocator.Find([@"C:\own"], [@"D:\path"], [@"C:\Program Files\Servy"], all));
        Assert.Equal(@"D:\path\servy-cli.exe", ServyLocator.Find([@"C:\nope"], [@"D:\path"], [@"C:\Program Files\Servy"], all));
        Assert.Equal(@"C:\Program Files\Servy\servy-cli.exe", ServyLocator.Find([], [@"D:\nope"], [@"C:\Program Files\Servy"], all));
    }

    [Fact]
    public void FindsAnInstallerInstall_EvenWhenItIsNotOnPath()
    {
        var known = ServyLocator.KnownDirs(@"C:\Program Files", @"C:\Program Files (x86)", @"C:\Users\me\AppData\Local");

        var found = ServyLocator.Find([], [], known, Exists(@"C:\Program Files\Servy\servy-cli.exe"));

        Assert.Equal(@"C:\Program Files\Servy\servy-cli.exe", found);
    }

    [Fact]
    public void NotInstalled_ReturnsNull()
    {
        Assert.Null(ServyLocator.Find([@"C:\own"], [@"D:\path"], [@"C:\k"], _ => false));
    }

    [Fact]
    public void IgnoresBlankAndQuotedPathEntries()
    {
        var found = ServyLocator.Find([], ["", "  ", "\"D:\\quoted\\servy\""], [], Exists(@"D:\quoted\servy\servy-cli.exe"));

        Assert.Equal(@"D:\quoted\servy\servy-cli.exe", found);
    }

    [Fact]
    public void KnownDirs_CoverProgramFilesBothBitnessesAndLocalAppData()
    {
        var dirs = ServyLocator.KnownDirs(@"C:\Program Files", @"C:\Program Files (x86)", @"C:\Users\me\AppData\Local");

        Assert.Contains(@"C:\Program Files\Servy", dirs);
        Assert.Contains(@"C:\Program Files (x86)\Servy", dirs);
        Assert.Contains(@"C:\Users\me\AppData\Local\Programs\Servy", dirs);
    }
}
