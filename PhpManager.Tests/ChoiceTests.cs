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
