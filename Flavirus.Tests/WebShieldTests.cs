using Flavirus.Core.Web;

namespace Flavirus.Tests;

public class WebShieldTests
{
    [Fact]
    public void Blocks_Domain_And_Its_Subdomains_But_Not_Lookalikes()
    {
        var shield = new WebShield();
        shield.LoadFromList(new[] { " example.test. " });

        Assert.True(shield.IsBlocked("https://example.test/"));
        Assert.True(shield.IsBlocked("https://login.example.test/account"));
        Assert.False(shield.IsBlocked("https://notexample.test/"));
        Assert.False(shield.IsBlocked("https://example.test.attacker.invalid/"));
    }

    [Fact]
    public void Url_Rule_Matches_Only_That_Host_And_Path()
    {
        var shield = new WebShield();
        shield.LoadFromList(new[] { "https://example.test/login" });

        Assert.True(shield.IsBlocked("https://example.test/login"));
        Assert.True(shield.IsBlocked("https://example.test/login/reset"));
        Assert.False(shield.IsBlocked("https://example.test/logins"));
        Assert.False(shield.IsBlocked("https://example.test.attacker.invalid/login"));
        Assert.False(shield.IsBlocked("http://example.test/login"));
    }

    [Fact]
    public void Url_Rule_With_Query_Matches_That_Query()
    {
        var shield = new WebShield();
        shield.LoadFromList(new[] { "https://example.test/search?q=blocked" });

        Assert.True(shield.IsBlocked("https://example.test/search?q=blocked"));
        Assert.False(shield.IsBlocked("https://example.test/search?q=allowed"));
    }
}
