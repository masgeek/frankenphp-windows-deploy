namespace PhpManager.Tests;

public class ComposerConstraintTests
{
    [Theory]
    [InlineData("^8.2", "8.2.0", true)]
    [InlineData("^8.2", "8.4.1", true)]
    [InlineData("^8.2", "9.0.0", false)]
    [InlineData("^8.2", "8.1.9", false)]
    [InlineData("^8.2.5", "8.2.4", false)]
    [InlineData("~8.2.0", "8.2.9", true)]
    [InlineData("~8.2.0", "8.3.0", false)]
    [InlineData("~8.2", "8.9.0", true)]
    [InlineData("~8.2", "9.0.0", false)]
    [InlineData(">=8.1", "8.1.0", true)]
    [InlineData(">=8.1", "7.4.33", false)]
    [InlineData(">= 8.1", "8.3.1", true)]
    [InlineData(">=8.1 <8.4", "8.3.9", true)]
    [InlineData(">=8.1 <8.4", "8.4.0", false)]
    [InlineData(">=8.1,<8.4", "8.2.0", true)]
    [InlineData("8.2.*", "8.2.30", true)]
    [InlineData("8.2.*", "8.3.0", false)]
    [InlineData("8.*", "8.9.9", true)]
    [InlineData("8.*", "9.0.0", false)]
    [InlineData("^8.1 || ^8.3", "8.3.5", true)]
    [InlineData("^7.4 || ^8.0", "8.4.1", true)]
    [InlineData("^7.4 | ^8.0", "7.4.33", true)]
    [InlineData("^7.4 || ^8.0", "9.0.0", false)]
    [InlineData("8.3.12", "8.3.12", true)]
    [InlineData("8.3.12", "8.3.13", false)]
    [InlineData("8.3", "8.3.20", true)]
    [InlineData("8.3", "8.4.0", false)]
    [InlineData("*", "5.6.40", true)]
    [InlineData("", "8.4.0", true)]
    [InlineData(">8.1", "8.1.5", false)]
    [InlineData(">8.1", "8.2.0", true)]
    [InlineData("<=8.2", "8.2.9", true)]
    [InlineData("!=8.2.1", "8.2.1", false)]
    [InlineData("^0.3", "0.3.9", true)]
    [InlineData("^0.3", "0.4.0", false)]
    public void Satisfies(string constraint, string version, bool expected)
    {
        Assert.Equal(expected, ComposerConstraint.Satisfies(constraint, Version.Parse(version)));
    }

    [Fact]
    public void Garbage_DoesNotSatisfy()
    {
        Assert.False(ComposerConstraint.Satisfies("banana", new Version(8, 3, 0)));
    }
}

public sealed class ProjectPhpTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "php-manager-proj-" + Guid.NewGuid().ToString("N"));

    public ProjectPhpTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void NoFiles_NoRequirement()
    {
        Assert.Null(ProjectPhp.ReadRequirement(_dir));
    }

    [Fact]
    public void ReadsRequirePhp_FromComposerJson()
    {
        File.WriteAllText(Path.Combine(_dir, "composer.json"), "{\"require\":{\"php\":\"^8.2\",\"laravel/framework\":\"^11.0\"}}");

        var req = ProjectPhp.ReadRequirement(_dir)!;

        Assert.Equal("^8.2", req.Constraint);
        Assert.Contains("require.php", req.Source);
    }

    [Fact]
    public void PlatformOverride_BeatsRequire()
    {
        File.WriteAllText(Path.Combine(_dir, "composer.json"),
            "{\"require\":{\"php\":\"^8.1\"},\"config\":{\"platform\":{\"php\":\"8.2.0\"}}}");

        Assert.Equal("8.2.0", ProjectPhp.ReadRequirement(_dir)!.Constraint);
    }

    [Fact]
    public void PhpVersionFile_BeatsComposer()
    {
        File.WriteAllText(Path.Combine(_dir, "composer.json"), "{\"require\":{\"php\":\"^8.1\"}}");
        File.WriteAllText(Path.Combine(_dir, ".php-version"), "8.3\n");

        var req = ProjectPhp.ReadRequirement(_dir)!;

        Assert.Equal("8.3", req.Constraint);
        Assert.Equal(".php-version", req.Source);
    }

    [Fact]
    public void BrokenComposerJson_IsIgnored()
    {
        File.WriteAllText(Path.Combine(_dir, "composer.json"), "{ not json");

        Assert.Null(ProjectPhp.ReadRequirement(_dir));
    }

    [Fact]
    public void Matching_ReturnsSatisfyingVersions_HighestFirst()
    {
        var result = ProjectPhp.Matching("^8.2", ["7.4.33", "8.2.5", "8.4.1", "8.3.10", "latest"]);

        Assert.Equal(["8.4.1", "8.3.10", "8.2.5"], result);
    }

    [Fact]
    public void WritePin_CreatesVersionFileAndShim()
    {
        ProjectPhp.WritePin(_dir, "8.3.10", "C:\\PHP\\8.3.10\\php.exe");

        Assert.Equal("8.3.10\n", File.ReadAllText(Path.Combine(_dir, ".php-version")));
        Assert.Contains("8.3.10\\php.exe\" %*", File.ReadAllText(Path.Combine(_dir, "php.cmd")));
    }
}

public class PhpSupportTests
{
    [Theory]
    [InlineData("8.4.1", "2026-11-19", SupportStatus.Active)]
    [InlineData("8.3.20", "2026-11-19", SupportStatus.SecurityOnly)]
    [InlineData("8.2.1", "2026-11-19", SupportStatus.SecurityOnly)]
    [InlineData("8.1.30", "2026-11-19", SupportStatus.EndOfLife)]
    [InlineData("7.4.33", "2026-11-19", SupportStatus.EndOfLife)]
    [InlineData("8.5.0", "2026-11-19", SupportStatus.Active)]
    [InlineData("9.9.0", "2026-11-19", SupportStatus.Unknown)]
    [InlineData("8.4.1", "2029-01-01", SupportStatus.EndOfLife)]
    public void StatusOf(string version, string today, SupportStatus expected)
    {
        Assert.Equal(expected, PhpSupport.StatusOf(version, DateOnly.Parse(today)));
    }

    [Fact]
    public void DoctorCheck_MapsStatusToSeverity()
    {
        var today = new DateOnly(2026, 11, 19);

        Assert.Equal(DoctorStatus.Pass, DoctorEvaluators.PhpSupportCheck("8.4.1", today).Status);
        Assert.Equal(DoctorStatus.Warn, DoctorEvaluators.PhpSupportCheck("8.3.1", today).Status);
        Assert.Equal(DoctorStatus.Fail, DoctorEvaluators.PhpSupportCheck("8.1.1", today).Status);
    }
}

public class UpdateCheckerTests
{
    [Fact]
    public void FindsHighestPatchInSameMinor()
    {
        var updates = UpdateChecker.FindUpdates(["8.3.1", "8.4.2"], ["8.3.1", "8.3.9", "8.3.12", "8.4.2", "8.5.0", "7.4.33"]);

        Assert.Equal([new PhpUpdate("8.3.1", "8.3.12")], updates);
    }

    [Fact]
    public void IgnoresNonVersionNames_AndNothingNewer()
    {
        Assert.Empty(UpdateChecker.FindUpdates(["latest", "8.4.2"], ["8.4.2", "8.4.1"]));
    }

    [Fact]
    public void Compares_Numerically_NotAlphabetically()
    {
        var updates = UpdateChecker.FindUpdates(["8.3.9"], ["8.3.10"]);

        Assert.Equal("8.3.10", Assert.Single(updates).Latest);
    }

    [Fact]
    public void MissingExtensionFiles_ListsOnlyWhatNewInstallLacks()
    {
        var root = Path.Combine(Path.GetTempPath(), "php-manager-upd-" + Guid.NewGuid().ToString("N"));
        var oldExt = Directory.CreateDirectory(Path.Combine(root, "old")).FullName;
        var newExt = Directory.CreateDirectory(Path.Combine(root, "new")).FullName;
        try
        {
            File.WriteAllText(Path.Combine(oldExt, "php_curl.dll"), "");
            File.WriteAllText(Path.Combine(oldExt, "php_redis.dll"), "");
            File.WriteAllText(Path.Combine(newExt, "php_curl.dll"), "");

            Assert.Equal(["php_redis.dll"], UpdateChecker.MissingExtensionFiles(oldExt, newExt));
            Assert.Empty(UpdateChecker.MissingExtensionFiles(Path.Combine(root, "nope"), newExt));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

public class LineDiffTests
{
    [Fact]
    public void IdenticalText_HasNoChanges()
    {
        Assert.False(LineDiff.HasChanges(LineDiff.Compute("a\nb\nc\n", "a\r\nb\r\nc\r\n")));
    }

    [Fact]
    public void DetectsChangedLine_AsRemovedThenAdded()
    {
        var diff = LineDiff.Compute("a\nmemory_limit = 128M\nc", "a\nmemory_limit = 512M\nc");

        Assert.Contains(new DiffLine(DiffKind.Removed, "memory_limit = 128M"), diff);
        Assert.Contains(new DiffLine(DiffKind.Added, "memory_limit = 512M"), diff);
        Assert.Equal(2, diff.Count(d => d.Kind is DiffKind.Added or DiffKind.Removed));
    }

    [Fact]
    public void DetectsInsertionsAndDeletions()
    {
        var diff = LineDiff.Compute("a\nb\nc\nd", "a\nc\nd\ne");

        Assert.Equal(["b"], diff.Where(d => d.Kind == DiffKind.Removed).Select(d => d.Text));
        Assert.Equal(["e"], diff.Where(d => d.Kind == DiffKind.Added).Select(d => d.Text));
    }

    [Fact]
    public void ReconstructsBothSides()
    {
        const string a = "1\n2\n3\n4\n5\n6";
        const string b = "1\n3\n4\nX\n5\n7\n8";

        var diff = LineDiff.Compute(a, b);

        Assert.Equal(a, string.Join("\n", diff.Where(d => d.Kind != DiffKind.Added).Select(d => d.Text)));
        Assert.Equal(b, string.Join("\n", diff.Where(d => d.Kind != DiffKind.Removed).Select(d => d.Text)));
    }

    [Fact]
    public void WithContext_KeepsNeighboursAndCollapsesTheRest()
    {
        var oldText = string.Join("\n", Enumerable.Range(1, 50).Select(i => "line" + i));
        var newText = oldText.Replace("line25", "CHANGED");

        var view = LineDiff.WithContext(LineDiff.Compute(oldText, newText), 2);

        Assert.Equal(["line23", "line24", "line25", "CHANGED", "line26", "line27"], view.Select(d => d.Text));
    }

    [Fact]
    public void WithContext_SeparatesDistantHunksWithAGap()
    {
        var oldText = string.Join("\n", Enumerable.Range(1, 50).Select(i => "line" + i));
        var newText = oldText.Replace("line5\n", "A\n").Replace("line45\n", "B\n");

        var view = LineDiff.WithContext(LineDiff.Compute(oldText, newText), 1);

        Assert.Single(view, d => d.Kind == DiffKind.Gap);
    }

    [Fact]
    public void HandlesLargeFilesQuickly()
    {
        var lines = Enumerable.Range(1, 2500).Select(i => "directive" + i + " = " + i).ToList();
        var changed = lines.ToList();
        changed[1000] = "changed";
        changed.RemoveAt(2000);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var diff = LineDiff.Compute(string.Join("\n", lines), string.Join("\n", changed));

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5));
        Assert.Equal(3, diff.Count(d => d.Kind is DiffKind.Added or DiffKind.Removed));
    }
}

public class PresetTests
{
    [Theory]
    [InlineData(LaravelProfile.Local)]
    [InlineData(LaravelProfile.Production)]
    public void LaravelPreset_AllValuesValid_AndKnown(LaravelProfile profile)
    {
        foreach (var (key, value) in LaravelPreset.Settings(profile))
        {
            var def = IniSettingDefs.Find(key);
            Assert.NotNull(def);
            Assert.True(IniSettingDefs.Validate(def!, value) == null, $"{key}={value} is invalid");
        }
    }

    [Fact]
    public void LaravelPreset_ProductionIsStricterThanLocal()
    {
        var local = LaravelPreset.Settings(LaravelProfile.Local);
        var prod = LaravelPreset.Settings(LaravelProfile.Production);

        Assert.Equal("On", local["display_errors"]);
        Assert.Equal("Off", prod["display_errors"]);
        Assert.Equal("Off", prod["opcache.validate_timestamps"]);
        Assert.Equal("On", local["opcache.validate_timestamps"]);
    }

    [Fact]
    public void LaravelPreset_UploadAndPostSizesAreConsistent()
    {
        var s = LaravelPreset.Settings(LaravelProfile.Local);

        Assert.True(IniSettingDefs.ToBytes(s["post_max_size"]) >= IniSettingDefs.ToBytes(s["upload_max_filesize"]));
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(4, 4)]
    [InlineData(8, 8)]
    [InlineData(64, 16)]
    [InlineData(0, 2)]
    public void FrankenPresets_ScaleWorkersToCpuCount(int cpus, int octaneWorkers)
    {
        var octane = FrankenPresets.For(cpus).Single(p => p.Name.StartsWith("Laravel"));

        Assert.Equal(octaneWorkers, octane.Workers);
    }

    [Fact]
    public void FrankenPresets_AreSane()
    {
        var all = FrankenPresets.For(8);

        Assert.Equal(3, all.Count);
        Assert.All(all, p => Assert.InRange(p.Workers, 1, 32));
        Assert.True(all.Single(p => p.Name == "Light development").DevMode);
    }
}

public class SettingsBundleTests
{
    [Fact]
    public void RoundTrips()
    {
        var bundle = new SettingsBundle
        {
            ExportedAt = "2026-11-19",
            BasePath = "C:\\PHP",
            PhpVersion = "8.4.1",
            Directives = new() { ["memory_limit"] = "512M" },
            EnabledExtensions = ["curl", "mbstring"],
            DisabledExtensions = ["gd"],
            OpcacheLoaded = true,
            UrlsJson = "[]"
        };

        var copy = SettingsBundle.FromJson(bundle.ToJson());

        Assert.Equal("512M", copy.Directives["memory_limit"]);
        Assert.Equal(["curl", "mbstring"], copy.EnabledExtensions);
        Assert.True(copy.OpcacheLoaded);
        Assert.Equal("C:\\PHP", copy.BasePath);
    }

    [Fact]
    public void RejectsGarbage_FutureVersions_AndBadDirectives()
    {
        Assert.Throws<InvalidDataException>(() => SettingsBundle.FromJson("not json"));
        Assert.Throws<InvalidDataException>(() => SettingsBundle.FromJson("null"));
        Assert.Throws<InvalidDataException>(() => SettingsBundle.FromJson("{\"Version\":99}"));
        Assert.Throws<InvalidDataException>(() => SettingsBundle.FromJson("{\"Version\":1,\"Directives\":{\"bad key\":\"x\"}}"));
        Assert.Throws<InvalidDataException>(() => SettingsBundle.FromJson("{\"Version\":1,\"Directives\":{\"memory_limit\":\"a\\nb\"}}"));
    }
}

public class DoctorEvaluatorTests
{
    [Fact]
    public void ExtensionDir_MissingAndRelativeAndAbsolute()
    {
        Assert.Equal(DoctorStatus.Warn, DoctorEvaluators.ExtensionDirCheck("[PHP]\n", "C:\\PHP\\8.4", _ => true).Status);

        var rel = DoctorEvaluators.ExtensionDirCheck("extension_dir = \"ext\"\n", "C:\\PHP\\8.4", d => d == "C:\\PHP\\8.4\\ext");
        Assert.Equal(DoctorStatus.Pass, rel.Status);

        var bad = DoctorEvaluators.ExtensionDirCheck("extension_dir = \"C:\\gone\\ext\"\n", "C:\\PHP\\8.4", _ => false);
        Assert.Equal(DoctorStatus.Fail, bad.Status);
        Assert.Equal("apply-profile", bad.FixId);
    }

    [Fact]
    public void CaCert_ConfiguredMissingOrAbsent()
    {
        Assert.Equal("cacert", DoctorEvaluators.CaCertCheck("[PHP]\n", _ => true).FixId);
        Assert.Equal(DoctorStatus.Pass, DoctorEvaluators.CaCertCheck("openssl.cafile = \"C:\\x\\cacert.pem\"\n", _ => true).Status);
        Assert.Equal(DoctorStatus.Fail, DoctorEvaluators.CaCertCheck("curl.cainfo = \"C:\\x\\cacert.pem\"\n", _ => false).Status);
    }

    [Fact]
    public void Path_ActiveFirstOtherSecondNone()
    {
        Func<string, bool> has = d => d.StartsWith("C:\\PHP") || d.StartsWith("C:\\xampp");

        Assert.Equal(DoctorStatus.Pass, DoctorEvaluators.PathCheck(["C:\\PHP\\8.4", "C:\\Windows"], "C:\\PHP\\8.4", has).Status);
        Assert.Equal(DoctorStatus.Fail, DoctorEvaluators.PathCheck(["C:\\xampp\\php", "C:\\PHP\\8.4"], "C:\\PHP\\8.4", has).Status);
        Assert.Equal(DoctorStatus.Warn, DoctorEvaluators.PathCheck(["C:\\PHP\\8.4", "C:\\xampp\\php"], "C:\\PHP\\8.4", has).Status);
        Assert.Equal("reapply-path", DoctorEvaluators.PathCheck(["C:\\Windows"], "C:\\PHP\\8.4", has).FixId);
        Assert.Equal("set-active", DoctorEvaluators.PathCheck(["C:\\PHP\\8.4"], null, has).FixId);
    }

    [Fact]
    public void Path_IgnoresTrailingSlashAndCase()
    {
        var result = DoctorEvaluators.PathCheck(["c:\\php\\8.4\\"], "C:\\PHP\\8.4", _ => true);

        Assert.Equal(DoctorStatus.Pass, result.Status);
    }

    [Fact]
    public void Composer_FoundOrOfferInstall()
    {
        Assert.Equal(DoctorStatus.Pass, DoctorEvaluators.ComposerCheck(["C:\\a", "C:\\composer"], f => f == "C:\\composer\\composer.bat").Status);
        Assert.Equal("install-composer", DoctorEvaluators.ComposerCheck(["C:\\a"], _ => false).FixId);
    }

    [Fact]
    public void Results_ExposeIconAndFixLabel()
    {
        var r = new DoctorResult("x", DoctorStatus.Fail, "d", "cacert");

        Assert.Equal("\u2717", r.Icon);
        Assert.True(r.CanFix);
        Assert.False(string.IsNullOrEmpty(r.FixLabel));
        Assert.False(new DoctorResult("x", DoctorStatus.Pass, "d").CanFix);
    }
}

public class ComposerServiceTests
{
    [Theory]
    [InlineData("Composer version 2.8.4 2025-01-01 12:00:00", "2.8.4")]
    [InlineData("Warning: x\nComposer version 2.7.1-RC1 2024", "2.7.1-RC1")]
    [InlineData("Composer 2.8.4", "2.8.4")]
    [InlineData("nope", null)]
    [InlineData(null, null)]
    public void ParseVersion(string? output, string? expected)
    {
        Assert.Equal(expected, ComposerService.ParseVersion(output));
    }

    [Fact]
    public void Shim_RunsPharNextToItself_ViaPath()
    {
        var shim = ComposerService.ShimContent();

        Assert.Contains("%~dp0composer.phar", shim);
        Assert.Contains("%*", shim);
        Assert.StartsWith("@echo off", shim);
    }
}
