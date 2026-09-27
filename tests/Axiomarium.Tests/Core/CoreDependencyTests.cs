using Axiomarium.Core.Assets;

namespace Axiomarium.Tests.Core;

public class CoreDependencyTests
{
    [Fact]
    public void Core_references_no_console_or_network_api()
    {
        var found = CoreDependencyScanner.FindBannedReferences(typeof(AssetKind).Assembly.Location);

        Assert.Empty(found);
    }

    [Fact]
    public void Scanner_finds_banned_references_in_the_canary()
    {
        var found = CoreDependencyScanner.FindBannedReferences(typeof(BannedApiCanary).Assembly.Location);

        Assert.Contains("System.Console", found);
        Assert.Contains("System.Net.Http.HttpClient", found);
    }
}
