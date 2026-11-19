namespace PhpManager.Tests;

public class ExtensionCatalogTests
{
    [Theory]
    [InlineData("pdo_mysql", ExtensionKind.Dll, ExtensionCatalog.Database)]
    [InlineData("sqlsrv", ExtensionKind.Dll, ExtensionCatalog.Database)]
    [InlineData("redis", ExtensionKind.Dll, ExtensionCatalog.Caching)]
    [InlineData("opcache", ExtensionKind.Zend, ExtensionCatalog.Caching)]
    [InlineData("xdebug", ExtensionKind.Zend, ExtensionCatalog.Debugging)]
    [InlineData("curl", ExtensionKind.Dll, ExtensionCatalog.Web)]
    [InlineData("openssl", ExtensionKind.Dll, ExtensionCatalog.Security)]
    [InlineData("mbstring", ExtensionKind.Dll, ExtensionCatalog.Text)]
    [InlineData("dom", ExtensionKind.BuiltIn, ExtensionCatalog.Xml)]
    [InlineData("gd", ExtensionKind.Dll, ExtensionCatalog.Files)]
    [InlineData("zip", ExtensionKind.Dll, ExtensionCatalog.Files)]
    [InlineData("gmp", ExtensionKind.Dll, ExtensionCatalog.Math)]
    public void KnownExtensions_AreGroupedByFunction(string name, ExtensionKind kind, string expected)
    {
        Assert.Equal(expected, ExtensionCatalog.GroupOf(name, kind));
    }

    [Fact]
    public void Lookup_IsCaseInsensitive()
    {
        Assert.Equal(ExtensionCatalog.Database, ExtensionCatalog.GroupOf("PDO", ExtensionKind.BuiltIn));
    }

    [Fact]
    public void UnknownBuiltIn_GoesToCore_UnknownDll_GoesToOther()
    {
        Assert.Equal(ExtensionCatalog.Core, ExtensionCatalog.GroupOf("standard", ExtensionKind.BuiltIn));
        Assert.Equal(ExtensionCatalog.Other, ExtensionCatalog.GroupOf("someweird", ExtensionKind.Dll));
    }

    [Fact]
    public void EveryGroup_HasASortPosition_AndOtherIsAfterCore()
    {
        Assert.All(ExtensionCatalog.Order, g => Assert.True(ExtensionCatalog.OrderOf(g) < ExtensionCatalog.Order.Length));
        Assert.True(ExtensionCatalog.OrderOf(ExtensionCatalog.Other) > ExtensionCatalog.OrderOf(ExtensionCatalog.Core));
        Assert.Equal(ExtensionCatalog.Order.Length, ExtensionCatalog.OrderOf("not a group"));
    }

    [Fact]
    public void ExtensionInfo_ExposesGroupAndOrder_ForTheGrid()
    {
        var ext = new ExtensionInfo { Name = "pdo_mysql" };

        Assert.Equal(ExtensionCatalog.Database, ext.Group);
        Assert.Equal(0, ext.GroupOrder);
    }

    [Fact]
    public void LaravelSet_IsAllCoveredByAGroup()
    {
        Assert.All(LaravelExtensions.Names, n => Assert.NotEqual(ExtensionCatalog.Other, ExtensionCatalog.GroupOf(n, ExtensionKind.Dll)));
    }
}
