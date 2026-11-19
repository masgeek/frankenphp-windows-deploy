namespace PhpManager.Tests;

public class PhpVersionParserTests
{
    [Fact]
    public void Parses_PlainOutput()
    {
        Assert.Equal((8, 4, 27), PhpVersionParser.Parse("PHP 8.4.27 (cli) (built: Oct 1 2025) (NTS Visual C++ 2022 x64)\nCopyright (c) The PHP Group\n"));
    }

    [Fact]
    public void Parses_WhenStartupWarningsComeFirst()
    {
        const string output = "\nWarning: PHP Startup: Unable to load dynamic library 'php_foo.dll' (tried: ext dir) in Unknown on line 0\r\nPHP 8.3.20 (cli) (built: Mar 1 2025)\r\n";

        Assert.Equal((8, 3, 20), PhpVersionParser.Parse(output));
    }

    [Fact]
    public void Parses_WithLeadingWhitespaceOrBom()
    {
        Assert.Equal((7, 4, 33), PhpVersionParser.Parse("\uFEFF  PHP 7.4.33 (cli)"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Warning: something broke")]
    [InlineData("The code execution cannot proceed because VCRUNTIME140.dll was not found.")]
    public void ReturnsNull_WhenNoVersionLine(string? output)
    {
        Assert.Null(PhpVersionParser.Parse(output));
    }
}
