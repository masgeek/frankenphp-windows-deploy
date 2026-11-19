namespace PhpManager.Tests;

public class IniEditorExtensionTests
{
    [Fact]
    public void Opcache_EnableUncommentsZendLine()
    {
        var result = PhpIniEditor.SetExtensionState("[PHP]\n;zend_extension=opcache\n", "opcache", enabled: true, zend: true);

        Assert.Equal("[PHP]\nzend_extension=opcache\n", result);
    }

    [Fact]
    public void Opcache_DisableCommentsOut_AndKeepsPathForm()
    {
        var result = PhpIniEditor.SetExtensionState("zend_extension=\"C:\\x\\php_opcache.dll\"\n", "opcache", enabled: false, zend: true);

        Assert.Equal(";zend_extension=\"C:\\x\\php_opcache.dll\"\n", result);
    }

    [Fact]
    public void Enable_MissingZendLine_IsAppended()
    {
        var result = PhpIniEditor.SetExtensionState("[PHP]\nfoo=1\n[Date]\n", "opcache", enabled: true, zend: true);

        Assert.True(result.IndexOf("zend_extension=opcache", StringComparison.Ordinal) < result.IndexOf("[Date]", StringComparison.Ordinal));
    }

    [Fact]
    public void ZendAndPlainKeys_DoNotCrossMatch()
    {
        var ini = "extension=opcache\n";

        var result = PhpIniEditor.SetExtensionState(ini, "opcache", enabled: true, zend: true);

        Assert.Contains("extension=opcache", result);
        Assert.Contains("zend_extension=opcache", result);
    }

    [Fact]
    public void Enable_WhenAlreadyActive_KeepsOnlyTheActiveLine()
    {
        const string ini = ";extension=curl\nextension=curl\n";

        Assert.Equal("extension=curl\n", PhpIniEditor.SetExtensionState(ini, "curl", enabled: true, zend: false));
    }

    [Fact]
    public void Enable_WithATemplateLineFirstAndActiveLineLater_DoesNotCreateTwoActiveLines()
    {
        const string ini = "[PHP]\n;extension=redis\nfoo=1\nextension=redis\n";

        var result = PhpIniEditor.SetExtensionState(ini, "redis", enabled: true, zend: false);

        Assert.Equal("[PHP]\nfoo=1\nextension=redis\n", result);
        Assert.Empty(PhpIniEditor.FindDuplicateExtensions(result));
    }

    [Fact]
    public void Enable_IsIdempotent_NeverAddsASecondLine()
    {
        var once = PhpIniEditor.SetExtensionState("[PHP]\n", "curl", enabled: true, zend: false);
        var twice = PhpIniEditor.SetExtensionState(once, "curl", enabled: true, zend: false);

        Assert.Equal(once, twice);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(twice, "extension=curl"));
    }

    [Fact]
    public void Disable_CollapsesDuplicatesToOneCommentedLine()
    {
        var result = PhpIniEditor.SetExtensionState("extension=mysqli\n;extension=mysqli\nextension=php_mysqli.dll\n", "mysqli", enabled: false, zend: false);

        Assert.Equal(";extension=mysqli\n", result);
    }

    [Fact]
    public void DeduplicateExtensions_KeepsActiveLine_AndLeavesOtherContentAlone()
    {
        const string ini = "[PHP]\n;extension=curl\nextension=curl\n;extension=gd\n;extension=gd\nmemory_limit = 128M\nextension=openssl\n";

        var result = PhpIniEditor.DeduplicateExtensions(ini);

        Assert.Equal("[PHP]\nextension=curl\n;extension=gd\nmemory_limit = 128M\nextension=openssl\n", result);
    }

    [Fact]
    public void DeduplicateExtensions_TreatsPhpPrefixAndDllSuffixAsTheSameName_AndZendSeparately()
    {
        const string ini = "extension=php_curl.dll\nextension=curl\nextension=opcache\nzend_extension=opcache\n";

        var result = PhpIniEditor.DeduplicateExtensions(ini);

        Assert.Equal("extension=php_curl.dll\nextension=opcache\nzend_extension=opcache\n", result);
    }

    [Fact]
    public void DeduplicateExtensions_WithNoDuplicates_ReturnsTheSameText()
    {
        const string ini = "[PHP]\r\nextension=curl\r\n;extension=gd\r\n";

        Assert.Same(ini, PhpIniEditor.DeduplicateExtensions(ini));
    }

    [Fact]
    public void FindDuplicateExtensions_ListsEachDuplicatedName()
    {
        var dups = PhpIniEditor.FindDuplicateExtensions(";extension=a\nextension=a\nextension=b\nextension=PHP_c.dll\n;extension=c\n");

        Assert.Equal(["extension=a", "extension=c"], dups.OrderBy(d => d));
    }

    [Fact]
    public void DoctorCheck_WarnsOnDuplicates_AndOffersFix()
    {
        var bad = DoctorEvaluators.DuplicateExtensionsCheck("extension=curl\n;extension=curl\n");
        var good = DoctorEvaluators.DuplicateExtensionsCheck("extension=curl\n");

        Assert.Equal(DoctorStatus.Warn, bad.Status);
        Assert.Equal("dedupe-ini", bad.FixId);
        Assert.Equal(DoctorStatus.Pass, good.Status);
    }

    [Fact]
    public void Disable_CommentsEveryActiveLine_ButNotPrefixes()
    {
        var result = PhpIniEditor.SetExtensionState("extension=pdo\nextension=pdo_mysql\nextension=pdo\n", "pdo", enabled: false, zend: false);

        Assert.Equal(";extension=pdo\nextension=pdo_mysql\n", result);
    }
}

public class IniEditorDirectiveTests
{
    [Fact]
    public void Get_ReturnsLastActiveValue_StrippingCommentsAndQuotes()
    {
        const string ini = "; post_max_size = 1M\npost_max_size = 8M\npost_max_size = 64M ; bumped\ndate.timezone = \"Africa/Lagos\"\n";

        Assert.Equal("64M", PhpIniEditor.GetDirective(ini, "post_max_size"));
        Assert.Equal("Africa/Lagos", PhpIniEditor.GetDirective(ini, "date.timezone"));
        Assert.Null(PhpIniEditor.GetDirective(ini, "memory_limit"));
    }

    [Fact]
    public void Get_IgnoresCommentedOnlyDirective()
    {
        Assert.Null(PhpIniEditor.GetDirective(";memory_limit = 128M\n", "memory_limit"));
    }

    [Fact]
    public void Set_ReplacesActiveValue()
    {
        var result = PhpIniEditor.SetDirective("[PHP]\nmemory_limit = 128M\nfoo = 1\n", "memory_limit", "512M");

        Assert.Equal("[PHP]\nmemory_limit = 512M\nfoo = 1\n", result);
    }

    [Fact]
    public void Set_UncommentsTemplateLine_WhenNoneActive()
    {
        var result = PhpIniEditor.SetDirective("[PHP]\n;date.timezone =\nfoo = 1\n", "date.timezone", "UTC");

        Assert.Equal("[PHP]\ndate.timezone = UTC\nfoo = 1\n", result);
    }

    [Fact]
    public void Set_AddsLine_BeforeNextSection_WhenAbsent()
    {
        var result = PhpIniEditor.SetDirective("[PHP]\nfoo = 1\n[CLI Server]\nbar = 2\n", "max_input_vars", "5000");

        Assert.True(result.IndexOf("max_input_vars = 5000", StringComparison.Ordinal) < result.IndexOf("[CLI Server]", StringComparison.Ordinal));
    }

    [Fact]
    public void Set_PreservesCrLf_AndDoesNotMatchPrefixKeys()
    {
        var result = PhpIniEditor.SetDirective("[PHP]\r\nopcache.enable_cli = 0\r\nopcache.enable = 1\r\n", "opcache.enable", "0");

        Assert.Equal("[PHP]\r\nopcache.enable_cli = 0\r\nopcache.enable = 0\r\n", result);
    }

    [Fact]
    public void SetThenGet_RoundTrips_AndKeepsExpressionsUnquoted()
    {
        var result = PhpIniEditor.SetDirective("[PHP]\n", "error_reporting", "E_ALL & ~E_DEPRECATED");

        Assert.Equal("E_ALL & ~E_DEPRECATED", PhpIniEditor.GetDirective(result, "error_reporting"));
    }
}

public sealed class IniBackupTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "php-manager-bk-" + Guid.NewGuid().ToString("N"));
    private string Ini => Path.Combine(_dir, "php.ini");

    public IniBackupTests()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Ini, "original");
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Write_BacksUpPreviousContent_ThenWrites()
    {
        PhpIniBackup.Write(Ini, "changed");

        Assert.Equal("changed", File.ReadAllText(Ini));
        var backup = Assert.Single(PhpIniBackup.List(Ini));
        Assert.Equal("original", File.ReadAllText(backup.Path));
    }

    [Fact]
    public void Backup_SkipsWhenUnchangedSinceLastBackup()
    {
        Assert.NotNull(PhpIniBackup.Backup(Ini));
        Assert.Null(PhpIniBackup.Backup(Ini));
        Assert.Single(PhpIniBackup.List(Ini));
    }

    [Fact]
    public void Backup_MissingIni_ReturnsNull()
    {
        File.Delete(Ini);

        Assert.Null(PhpIniBackup.Backup(Ini));
        Assert.Empty(PhpIniBackup.List(Ini));
    }

    [Fact]
    public void Copy_BacksUpThenReplaces()
    {
        var template = Path.Combine(_dir, "php.ini-production");
        File.WriteAllText(template, "from template");

        PhpIniBackup.Copy(template, Ini);

        Assert.Equal("from template", File.ReadAllText(Ini));
        Assert.Equal("original", File.ReadAllText(PhpIniBackup.List(Ini)[0].Path));
    }

    [Fact]
    public void Restore_RollsBack_AndBacksUpCurrentSoItCanBeUndone()
    {
        PhpIniBackup.Write(Ini, "v2");
        PhpIniBackup.Write(Ini, "v3");
        var original = PhpIniBackup.List(Ini).Last(); // oldest = "original"

        PhpIniBackup.Restore(Ini, original.Path);

        Assert.Equal("original", File.ReadAllText(Ini));
        Assert.Contains(PhpIniBackup.List(Ini), b => File.ReadAllText(b.Path) == "v3");
    }

    [Fact]
    public void Restore_RejectsFilesOutsideBackupFolder()
    {
        var outside = Path.Combine(_dir, "evil.bak");
        File.WriteAllText(outside, "x");

        Assert.Throws<FileNotFoundException>(() => PhpIniBackup.Restore(Ini, outside));
        Assert.Equal("original", File.ReadAllText(Ini));
    }

    [Fact]
    public void List_IsNewestFirst_AndPrunedToMax()
    {
        var start = new DateTime(2026, 1, 1, 12, 0, 0);
        for (var i = 0; i < PhpIniBackup.MaxBackups + 5; i++)
        {
            File.WriteAllText(Ini, "content " + i);
            PhpIniBackup.Backup(Ini, start.AddSeconds(i));
            File.WriteAllText(Ini, "next " + i);
        }

        var list = PhpIniBackup.List(Ini);

        Assert.Equal(PhpIniBackup.MaxBackups, list.Count);
        Assert.True(list[0].Created > list[^1].Created);
        Assert.Equal("content " + (PhpIniBackup.MaxBackups + 4), File.ReadAllText(list[0].Path));
    }
}

public class IniSettingDefsTests
{
    [Theory]
    [InlineData("post_max_size", "64M", true)]
    [InlineData("post_max_size", "2g", true)]
    [InlineData("memory_limit", "-1", true)]
    [InlineData("post_max_size", "64MB", false)]
    [InlineData("post_max_size", "big", false)]
    [InlineData("max_execution_time", "300", true)]
    [InlineData("max_execution_time", "5m", false)]
    [InlineData("max_input_time", "-1", true)]
    [InlineData("display_errors", "On", true)]
    [InlineData("display_errors", "off", true)]
    [InlineData("display_errors", "yes", false)]
    [InlineData("date.timezone", "Africa/Lagos", true)]
    [InlineData("date.timezone", "", false)]
    public void Validate_AcceptsAndRejects(string key, string value, bool ok)
    {
        var setting = IniSettingDefs.Find(key)!;

        Assert.Equal(ok, IniSettingDefs.Validate(setting, value) == null);
    }

    [Theory]
    [InlineData("128M", 134217728L)]
    [InlineData("2G", 2147483648L)]
    [InlineData("512k", 524288L)]
    [InlineData("1024", 1024L)]
    public void ToBytes_ParsesPhpSizes(string input, long expected)
    {
        Assert.Equal(expected, IniSettingDefs.ToBytes(input));
    }

    [Fact]
    public void ToBytes_UnlimitedAndInvalid()
    {
        Assert.Equal(IniSettingDefs.Unlimited, IniSettingDefs.ToBytes("-1"));
        Assert.Null(IniSettingDefs.ToBytes("lots"));
        Assert.Null(IniSettingDefs.ToBytes(null));
    }

    [Theory]
    [InlineData("session.gc_maxlifetime", true)]
    [InlineData("opcache.jit_buffer_size", true)]
    [InlineData("_private", true)]
    [InlineData("bad key", false)]
    [InlineData("1abc", false)]
    [InlineData("a=b", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValidKey_OnlyAllowsDirectiveNames(string? key, bool expected)
    {
        Assert.Equal(expected, IniSettingDefs.IsValidKey(key));
    }

    [Fact]
    public void Normalize_FixesBoolAndSizeCasing()
    {
        Assert.Equal("On", IniSettingDefs.Normalize(IniSettingDefs.Find("display_errors")!, "on"));
        Assert.Equal("Off", IniSettingDefs.Normalize(IniSettingDefs.Find("display_errors")!, "OFF"));
        Assert.Equal("64M", IniSettingDefs.Normalize(IniSettingDefs.Find("post_max_size")!, " 64m "));
    }

    [Fact]
    public void AllSettings_HaveUniqueKeys_AndValidDefaults()
    {
        Assert.Equal(IniSettingDefs.All.Length, IniSettingDefs.All.Select(s => s.Key).Distinct().Count());
        Assert.All(IniSettingDefs.All, s => Assert.True(IniSettingDefs.IsValidKey(s.Key)));
    }
}
