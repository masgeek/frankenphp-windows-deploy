using System.Text;

namespace PhpManager.Tests;

public sealed class TailLinesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "php-manager-tail-" + Guid.NewGuid().ToString("N"));

    public TailLinesTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Write(string content)
    {
        var file = Path.Combine(_dir, "stdout.log");
        File.WriteAllText(file, content);
        return file;
    }

    [Fact]
    public void ReturnsLastNLines()
    {
        var file = Write(string.Join("\r\n", Enumerable.Range(1, 10).Select(i => "line" + i)) + "\r\n");

        Assert.Equal(["line8", "line9", "line10"], FeatureService.TailLines(file, 3));
    }

    [Fact]
    public void ShortFile_ReturnsEverything_WithoutTrailingBlankLines()
    {
        var file = Write("a\nb\n\n\n");

        Assert.Equal(["a", "b"], FeatureService.TailLines(file, 50));
    }

    [Fact]
    public void EmptyFile_ReturnsNothing()
    {
        Assert.Empty(FeatureService.TailLines(Write(""), 10));
    }

    [Fact]
    public void WorksWhileAnotherProcessHasTheFileOpenForWriting()
    {
        var file = Path.Combine(_dir, "live.log");
        using var writer = new FileStream(file, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
        var bytes = Encoding.UTF8.GetBytes("one\ntwo\nthree\n");
        writer.Write(bytes);
        writer.Flush();

        Assert.Equal(["two", "three"], FeatureService.TailLines(file, 2));
    }

    [Fact]
    public void HugeFile_OnlyReadsTheTail_AndDropsThePartialFirstLine()
    {
        var line = new string('x', 99);
        var file = Write(string.Join("\n", Enumerable.Range(0, 20000).Select(i => $"{i:D5}{line}")));

        var tail = FeatureService.TailLines(file, 5);

        Assert.Equal(5, tail.Count);
        Assert.StartsWith("19999", tail[^1]);
        Assert.All(tail, l => Assert.Equal(104, l.Length));
    }
}

public sealed class BundleAndUpgradeHelperTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "php-manager-upg-" + Guid.NewGuid().ToString("N"));

    public BundleAndUpgradeHelperTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Bundle_IniPart_AppliesDirectivesAndExtensionsToIniText()
    {
        // mirrors what ApplyBundle does to php.ini text
        var bundle = new SettingsBundle
        {
            Directives = new() { ["memory_limit"] = "512M", ["post_max_size"] = "64M" },
            EnabledExtensions = ["curl", "intl"],
            DisabledExtensions = ["gd"],
            OpcacheLoaded = true
        };
        var ini = "[PHP]\nmemory_limit = 128M\n;extension=curl\nextension=gd\n;zend_extension=opcache\n";

        var content = ini;
        foreach (var (key, value) in bundle.Directives)
            content = PhpIniEditor.SetDirective(content, key, value);
        content = LaravelExtensions.Enable(content, bundle.EnabledExtensions);
        content = PhpIniEditor.DisableExtensions(content, bundle.DisabledExtensions);
        content = PhpIniEditor.SetExtensionState(content, "opcache", bundle.OpcacheLoaded, zend: true);

        Assert.Equal("512M", PhpIniEditor.GetDirective(content, "memory_limit"));
        Assert.Equal("64M", PhpIniEditor.GetDirective(content, "post_max_size"));
        Assert.Contains("\nextension=curl\n", content);
        Assert.Contains("extension=intl", content);
        Assert.Contains(";extension=gd", content);
        Assert.Contains("\nzend_extension=opcache\n", content);
    }

    [Fact]
    public void Bundle_DirectivesFailingValidation_AreRejectedByTheSameRulesTheUiUses()
    {
        var def = IniSettingDefs.Find("memory_limit")!;

        Assert.NotNull(IniSettingDefs.Validate(def, "lots"));
        Assert.Null(IniSettingDefs.Validate(def, "512M"));
    }

    [Fact]
    public void UpgradeCarriesOverOnlyExtraExtensionFiles()
    {
        var oldExt = Directory.CreateDirectory(Path.Combine(_root, "8.3.1", "ext")).FullName;
        var newExt = Directory.CreateDirectory(Path.Combine(_root, "8.3.12", "ext")).FullName;
        foreach (var f in new[] { "php_curl.dll", "php_redis.dll", "php_xdebug.dll" })
            File.WriteAllText(Path.Combine(oldExt, f), f);
        File.WriteAllText(Path.Combine(newExt, "php_curl.dll"), "new curl");

        foreach (var file in UpdateChecker.MissingExtensionFiles(oldExt, newExt))
            File.Copy(Path.Combine(oldExt, file), Path.Combine(newExt, file), overwrite: false);

        Assert.Equal("new curl", File.ReadAllText(Path.Combine(newExt, "php_curl.dll")));
        Assert.True(File.Exists(Path.Combine(newExt, "php_redis.dll")));
        Assert.True(File.Exists(Path.Combine(newExt, "php_xdebug.dll")));
    }

    [Fact]
    public void UpgradedIni_GetsItsExtensionDirRepointed()
    {
        var oldDir = Directory.CreateDirectory(Path.Combine(_root, "old-install")).FullName;
        var newDir = Directory.CreateDirectory(Path.Combine(_root, "new-install")).FullName;
        var ini = Path.Combine(newDir, "php.ini");
        File.WriteAllText(ini, "extension_dir = \"" + Path.Combine(oldDir, "ext") + "\"\n");

        PhpService.PatchExtensionDir(ini, newDir, backup: false);

        Assert.Contains(Path.Combine(newDir, "ext"), File.ReadAllText(ini));
        Assert.DoesNotContain(oldDir, File.ReadAllText(ini));
    }
}
