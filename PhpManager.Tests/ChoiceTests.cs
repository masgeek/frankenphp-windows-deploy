namespace PhpManager.Tests;

public class SettingChoiceTests
{
    [Fact]
    public void EveryPresetChoice_PassesItsOwnValidation()
    {
        foreach (var setting in IniSettingDefs.All)
            foreach (var option in setting.Options)
                Assert.True(IniSettingDefs.Validate(setting, option) == null, $"{setting.Key}: '{option}' is rejected");
    }

    [Fact]
    public void EverySetting_OffersAWayToSetIt_WithoutTyping_OrAllowsCustom()
    {
        foreach (var setting in IniSettingDefs.All)
            Assert.True(setting.Options.Length > 0 || setting.CanBeCustom, $"{setting.Key} has no way to be set");
    }

    [Fact]
    public void BoolSettings_AreOnOff_WithoutCustom()
    {
        foreach (var setting in IniSettingDefs.All.Where(s => s.Kind == IniValueKind.Bool))
        {
            Assert.Equal(["On", "Off"], setting.Options);
            Assert.False(setting.CanBeCustom);
        }
    }

    [Fact]
    public void FixedListSettings_DoNotAllowCustom()
    {
        Assert.False(IniSettingDefs.Find("opcache.jit")!.CanBeCustom);
        Assert.False(IniSettingDefs.Find("zend.assertions")!.CanBeCustom);
        Assert.True(IniSettingDefs.Find("post_max_size")!.CanBeCustom);
    }

    [Fact]
    public void Timezones_AreUniqueIanaNames_IncludingUtcAndLagos()
    {
        Assert.Equal(IniSettingDefs.TimeZones.Length, IniSettingDefs.TimeZones.Distinct().Count());
        Assert.Contains("UTC", IniSettingDefs.TimeZones);
        Assert.Contains("Africa/Lagos", IniSettingDefs.TimeZones);
        Assert.All(IniSettingDefs.TimeZones, z => Assert.True(z == "UTC" || z.Contains('/')));
    }

    [Fact]
    public void CommonUploadSizes_AreOffered()
    {
        var upload = IniSettingDefs.Find("upload_max_filesize")!.Options;

        Assert.Contains("64M", upload);
        Assert.Contains("128M", upload);
    }

    [Fact]
    public void MemoryLimit_OffersUnlimited()
    {
        Assert.Contains("-1", IniSettingDefs.Find("memory_limit")!.Options);
    }
}

public class XdebugModesTests
{
    [Fact]
    public void Parse_SplitsTrimsAndIgnoresUnknown()
    {
        var modes = XdebugModes.Parse(" debug , Develop,bogus ");

        Assert.Equal(["debug", "develop"], modes.OrderBy(m => m));
    }

    [Theory]
    [InlineData("", "off")]
    [InlineData(null, "off")]
    [InlineData("off", "off")]
    public void Parse_Format_EmptyMeansOff(string? input, string expected)
    {
        Assert.Equal(expected, XdebugModes.Format(XdebugModes.Parse(input)));
    }

    [Fact]
    public void Format_UsesCanonicalOrder_AndRoundTrips()
    {
        var formatted = XdebugModes.Format(["coverage", "debug", "develop"]);

        Assert.Equal("develop,debug,coverage", formatted);
        Assert.Equal(["coverage", "debug", "develop"], XdebugModes.Parse(formatted).OrderBy(m => m));
    }
}

public class ExtensionDllSelectorTests
{
    // The layout of the Microsoft msphpsql release zip: one DLL per PHP version, thread-safety and architecture.
    private static readonly string[] SqlsrvZip =
    [
        @"x64\php_pdo_sqlsrv_81_nts.dll", @"x64\php_pdo_sqlsrv_81_ts.dll",
        @"x64\php_pdo_sqlsrv_82_nts.dll", @"x64\php_pdo_sqlsrv_82_ts.dll",
        @"x64\php_pdo_sqlsrv_83_nts.dll", @"x64\php_pdo_sqlsrv_83_ts.dll",
        @"x64\php_pdo_sqlsrv_84_nts.dll", @"x64\php_pdo_sqlsrv_84_ts.dll",
        @"x86\php_pdo_sqlsrv_83_nts.dll", @"x86\php_pdo_sqlsrv_83_ts.dll",
        @"x86\php_pdo_sqlsrv_84_nts.dll", @"x86\php_pdo_sqlsrv_84_ts.dll"
    ];

    [Theory]
    [InlineData("8.3", "nts", "x64", @"x64\php_pdo_sqlsrv_83_nts.dll")]
    [InlineData("8.3", "ts", "x64", @"x64\php_pdo_sqlsrv_83_ts.dll")]
    [InlineData("8.1", "nts", "x64", @"x64\php_pdo_sqlsrv_81_nts.dll")]
    [InlineData("8.4", "nts", "x86", @"x86\php_pdo_sqlsrv_84_nts.dll")]
    public void ChoosesTheBuildMatchingVersionThreadSafetyAndArchitecture(string mm, string ts, string arch, string expected)
    {
        Assert.Equal(expected, ExtensionDllSelector.Choose(SqlsrvZip, mm, ts, arch));
    }

    [Fact]
    public void NoBuildForThisPhp_ReturnsNull_RatherThanTheWrongOne()
    {
        Assert.Null(ExtensionDllSelector.Choose(SqlsrvZip, "7.4", "nts", "x64"));
        Assert.Null(ExtensionDllSelector.Choose(SqlsrvZip, "8.5", "nts", "x64"));
        Assert.Null(ExtensionDllSelector.Choose(SqlsrvZip.Where(p => p.StartsWith("x64")), "8.3", "nts", "x86"));
    }

    [Fact]
    public void OldNaive_AlphabeticallyLast_WouldHaveBeenWrong()
    {
        var naive = SqlsrvZip.OrderByDescending(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase).First();

        Assert.NotEqual(ExtensionDllSelector.Choose(SqlsrvZip, "8.3", "nts", "x64"), naive);
    }

    [Fact]
    public void ArchitectureCanBeInTheFileName_OrJustTheFolder()
    {
        var files = new[] { "php_redis-6.3.0-8.3-nts-vs16-x86.dll", "php_redis-6.3.0-8.3-nts-vs16-x64.dll" };

        Assert.Equal("php_redis-6.3.0-8.3-nts-vs16-x64.dll", ExtensionDllSelector.Choose(files, "8.3", "nts", "x64"));
    }

    [Fact]
    public void SingleUnlabelledDll_IsAccepted()
    {
        Assert.Equal("php_redis.dll", ExtensionDllSelector.Choose(["php_redis.dll"], "8.3", "nts", "x64"));
    }

    [Fact]
    public void MoreSpecificFile_BeatsAnUnlabelledOne()
    {
        var chosen = ExtensionDllSelector.Choose(["php_x.dll", "x64/php_x_83_nts.dll"], "8.3", "nts", "x64");

        Assert.Equal("x64/php_x_83_nts.dll", chosen);
    }

    [Fact]
    public void Parse_ReadsVersionThreadSafetyAndArch()
    {
        var v = ExtensionDllSelector.Parse(@"C:\tmp\x64\php_sqlsrv_82_ts.dll");

        Assert.Equal("8.2", v.MajorMinor);
        Assert.Equal("ts", v.ThreadSafety);
        Assert.Equal("x64", v.Arch);
    }

    [Fact]
    public void Available_ListsTheOfferedBuilds_ForErrorMessages()
    {
        var text = ExtensionDllSelector.Available(SqlsrvZip);

        Assert.Contains("PHP 8.1 NTS x64", text);
        Assert.Contains("PHP 8.4 TS x86", text);
    }

    [Fact]
    public void CopyBest_CopiesTheMatchingBuild_AndRejectsWhenNoneMatches()
    {
        var root = Path.Combine(Path.GetTempPath(), "php-manager-dll-" + Guid.NewGuid().ToString("N"));
        var pkg = Path.Combine(root, "pkg");
        var ext = Directory.CreateDirectory(Path.Combine(root, "ext")).FullName;
        try
        {
            foreach (var rel in SqlsrvZip)
            {
                var path = Path.Combine(pkg, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, rel); // content records which build this is
            }

            ExtensionDllSelector.CopyBest(pkg, ext, "pdo_sqlsrv", null, ("8.3", "nts", "x64"));

            Assert.Equal(@"x64\php_pdo_sqlsrv_83_nts.dll", File.ReadAllText(Path.Combine(ext, "php_pdo_sqlsrv.dll")));

            var ex = Assert.Throws<FileNotFoundException>(() =>
                ExtensionDllSelector.CopyBest(pkg, ext, "pdo_sqlsrv", null, ("7.4", "nts", "x64")));
            Assert.Contains("7.4", ex.Message);
            Assert.Contains("PHP 8.3 NTS x64", ex.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
