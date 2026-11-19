namespace PhpManager.Tests;

public class PathEditorTests
{
    [Fact]
    public void RemoveEntry_RemovesMatchingDirectory()
    {
        Assert.Equal(@"C:\a;C:\c", PathEditor.RemoveEntry(@"C:\a;C:\PHP\8.4.1;C:\c", @"C:\PHP\8.4.1"));
    }

    [Theory]
    [InlineData(@"C:\PHP\8.4.1\")]
    [InlineData(@"c:\php\8.4.1")]
    [InlineData(@"""C:\PHP\8.4.1""")]
    public void RemoveEntry_IgnoresCaseTrailingSlashAndQuotes(string entry)
    {
        Assert.Equal(@"C:\a", PathEditor.RemoveEntry($@"C:\a;{entry}", @"C:\PHP\8.4.1"));
    }

    [Fact]
    public void RemoveEntry_DoesNotTouchSimilarDirectories()
    {
        const string path = @"C:\PHP\8.4.10;C:\PHP\8.4.1\bin;C:\PHP";

        Assert.Equal(path, PathEditor.RemoveEntry(path, @"C:\PHP\8.4.1"));
    }

    [Fact]
    public void RemoveEntry_RemovesDuplicates_AndHandlesNullOrEmpty()
    {
        Assert.Equal("", PathEditor.RemoveEntry(@"C:\x;C:\x\", @"C:\x"));
        Assert.Equal("", PathEditor.RemoveEntry(null, @"C:\x"));
    }

    [Fact]
    public void Contains_DetectsEntry()
    {
        Assert.True(PathEditor.Contains(@"C:\a;C:\PHP\8.4.1\", @"C:\PHP\8.4.1"));
        Assert.False(PathEditor.Contains(@"C:\a;C:\PHP\8.4.10", @"C:\PHP\8.4.1"));
        Assert.False(PathEditor.Contains(null, @"C:\PHP\8.4.1"));
    }
}

public class ManagedPhpEntryTests
{
    private static readonly Func<string, bool> HasPhp = d => d.EndsWith(@"\latest", StringComparison.OrdinalIgnoreCase);

    [Theory]
    [InlineData(@"C:\PHP\8.4.27", true)]
    [InlineData(@"C:\PHP\8.4.27\", true)]
    [InlineData(@"c:\php\8.3.1", true)]
    [InlineData(@"C:\PHP\latest", true)]
    [InlineData(@"C:\PHP\composer", false)]
    [InlineData(@"C:\PHP\.php-ini-backups", false)]
    [InlineData(@"C:\PHP\frankenphp", false)]
    [InlineData(@"C:\PHP", false)]
    [InlineData(@"C:\PHP\8.4.27\bin", false)]
    [InlineData(@"C:\tools\8.4.27", false)]
    public void IsManagedPhpEntry(string entry, bool expected)
    {
        Assert.Equal(expected, PathEditor.IsManagedPhpEntry(entry, @"C:\PHP", HasPhp));
    }
}
