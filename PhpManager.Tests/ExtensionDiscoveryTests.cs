namespace PhpManager.Tests;

public class ExtensionDiscoveryTests
{
    [Fact]
    public void ListsIniEntries_WithEnabledState()
    {
        var list = ExtensionDiscovery.Build("[PHP]\n;extension=curl\nextension=openssl\n", []);

        Assert.False(list.Single(e => e.Name == "curl").Enabled);
        Assert.True(list.Single(e => e.Name == "openssl").Enabled);
    }

    [Fact]
    public void IncludesDllsAddedLater_AsDisabled()
    {
        var list = ExtensionDiscovery.Build(";extension=curl\n", ["php_curl.dll", "php_redis.dll", "php_sqlsrv.dll"]);

        Assert.Equal(["curl", "redis", "sqlsrv"], list.Select(e => e.Name));
        Assert.False(list.Single(e => e.Name == "redis").Enabled);
    }

    [Fact]
    public void ActiveLine_WinsOverCommentedDuplicate_AndNoDuplicateRows()
    {
        var list = ExtensionDiscovery.Build(";extension=redis\nextension=redis\n", ["php_redis.dll"]);

        var redis = Assert.Single(list);
        Assert.True(redis.Enabled);
    }

    [Fact]
    public void OpcacheAndXdebug_AreZendKind_EnabledFromZendExtensionLines()
    {
        var list = ExtensionDiscovery.Build("zend_extension=opcache\n;zend_extension=xdebug\n", ["php_opcache.dll", "php_xdebug.dll"]);

        var opcache = list.Single(e => e.Name == "opcache");
        var xdebug = list.Single(e => e.Name == "xdebug");
        Assert.Equal(ExtensionKind.Zend, opcache.Kind);
        Assert.True(opcache.Enabled);
        Assert.Equal(ExtensionKind.Zend, xdebug.Kind);
        Assert.False(xdebug.Enabled);
    }

    [Fact]
    public void ZendDll_WithoutIniLine_IsListedDisabledAsZend_AndTestDllIsHidden()
    {
        var list = ExtensionDiscovery.Build("", ["php_opcache.dll", "php_zend_test.dll", "php_gd.dll"]);

        Assert.Equal(["gd", "opcache"], list.Select(e => e.Name));
        Assert.Equal(ExtensionKind.Zend, list.Single(e => e.Name == "opcache").Kind);
        Assert.False(list.Single(e => e.Name == "opcache").Enabled);
    }

    [Fact]
    public void ZendExtensionWithFullPath_IsRecognised()
    {
        var list = ExtensionDiscovery.Build("zend_extension=\"C:\\PHP\\ext\\php_xdebug.dll\"\n", []);

        var xdebug = Assert.Single(list);
        Assert.Equal("xdebug", xdebug.Name);
        Assert.True(xdebug.Enabled);
    }

    [Fact]
    public void BuiltInModules_AreListedEnabledAndReadOnly()
    {
        var list = ExtensionDiscovery.Build("extension=curl\n", ["php_curl.dll"], ["Core", "ctype", "curl", "json", "Zend OPcache", "xdebug"]);

        Assert.Equal(["ctype", "curl", "json"], list.Select(e => e.Name));
        var ctype = list.Single(e => e.Name == "ctype");
        Assert.True(ctype.IsBuiltIn);
        Assert.True(ctype.Enabled);
        Assert.False(list.Single(e => e.Name == "curl").IsBuiltIn);
    }

    [Fact]
    public void HandlesPhpPrefixDllSuffix_AndIsSorted()
    {
        var list = ExtensionDiscovery.Build("extension=php_zip.dll\r\n;extension=bz2\r\n", []);

        Assert.Equal(["bz2", "zip"], list.Select(e => e.Name));
        Assert.True(list.Single(e => e.Name == "zip").Enabled);
    }
}
