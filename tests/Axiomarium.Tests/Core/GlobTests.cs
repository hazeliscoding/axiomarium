using Axiomarium.Core.Paths;

namespace Axiomarium.Tests.Core;

public class GlobTests
{
    [Theory]
    [InlineData("src/api/**", "src/api/orders.cs", true)]
    [InlineData("src/api/**", "src/api/v1/orders.cs", true)]
    [InlineData("src/api/**", "src/apix/orders.cs", false)]
    [InlineData("src/api/", "src/api/v1/orders.cs", true)]
    [InlineData("src/api/", "src/apix/orders.cs", false)]
    [InlineData("*.md", "README.md", true)]
    [InlineData("*.md", "docs/a.md", false)]
    [InlineData("**/*.md", "a.md", true)]
    [InlineData("**/*.md", "docs/guide/a.md", true)]
    [InlineData("src/**/test/*.cs", "src/test/a.cs", true)]
    [InlineData("src/**/test/*.cs", "src/x/y/test/a.cs", true)]
    [InlineData("src/**/test/*.cs", "src/x/test/y/a.cs", false)]
    [InlineData("src/?.cs", "src/a.cs", true)]
    [InlineData("src/?.cs", "src/ab.cs", false)]
    [InlineData("src?a.cs", "src/a.cs", false)]
    [InlineData("src/{api,billing}/**", "src/billing/invoice.cs", true)]
    [InlineData("src/{api,billing}/**", "src/web/index.cs", false)]
    [InlineData("src/*.{cs,md}", "src/a.md", true)]
    [InlineData("src/{a,b/{c,d}}.cs", "src/b/d.cs", true)]
    [InlineData("docs/a+b (1).md", "docs/a+b (1).md", true)]
    [InlineData("docs/a+b (1).md", "docs/aab (1).md", false)]
    [InlineData("src/api/**", "SRC/api/orders.cs", false)]
    [InlineData("README.md", "README.md", true)]
    [InlineData("README.md", "docs/README.md", false)]
    public void Matches_relative_paths(string pattern, string path, bool expected)
    {
        Assert.True(Glob.TryParse(pattern, out var glob, out _));

        Assert.Equal(expected, glob!.IsMatch(path));
    }

    [Theory]
    [InlineData("src/{api", "'{' at column 5 is never closed.")]
    [InlineData("src/api}/**", "'}' at column 8 has no '{' before it.")]
    [InlineData("", "The pattern is empty.")]
    public void Invalid_pattern_says_why(string pattern, string expected)
    {
        Assert.False(Glob.TryParse(pattern, out var glob, out var problem));

        Assert.Null(glob);
        Assert.Equal(expected, problem);
    }

    [Fact]
    public void A_glob_remembers_its_pattern()
    {
        Assert.True(Glob.TryParse("src/api/**", out var glob, out _));

        Assert.Equal("src/api/**", glob!.Pattern);
    }
}
