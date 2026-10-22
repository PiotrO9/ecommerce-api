using E_commerce_backend.Services.Development;

namespace E_commerce_backend.Tests.Unit;

public sealed class DemoSeedAccessTests
{
    private const string ConfiguredKey = "a-demo-key-with-at-least-32-characters";

    [Fact]
    public void MatchingKeyAllowsSeedRequest()
    {
        Assert.True(DemoSeedAccess.IsAuthorized(ConfiguredKey, ConfiguredKey));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("wrong-key")]
    [InlineData("a-demo-key-with-at-least-32-characterS")]
    public void MissingOrDifferentKeyRejectsSeedRequest(string? suppliedKey)
    {
        Assert.False(DemoSeedAccess.IsAuthorized(suppliedKey, ConfiguredKey));
    }
}
