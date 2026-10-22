using E_commerce_backend.Services.Development;

namespace E_commerce_backend.Tests.Unit;

public sealed class DemoSeedOptionsTests
{
    [Fact]
    public void MissingBodyUsesMediumCatalogAndLowVariants()
    {
        Assert.True(DemoSeedOptions.TryCreate(null, out var options, out var error));
        Assert.Null(error);
        Assert.Equal(new DemoSeedOptions(new(25, 40), new(200, 300), new(1, 2)), options);
    }

    [Fact]
    public void LevelsCanBeSelectedIndependently()
    {
        var request = new DemoSeedRequest { Categories = " HIGH ", Products = "med", Variants = "medium" };

        Assert.True(DemoSeedOptions.TryCreate(request, out var options, out var error));
        Assert.Null(error);
        Assert.Equal(new DemoSeedOptions(new(50, 80), new(200, 300), new(3, 4)), options);
    }

    [Fact]
    public void ExplicitLowDoesNotChangeDefaultsOfMissingFields()
    {
        var request = new DemoSeedRequest { Products = "low" };

        Assert.True(DemoSeedOptions.TryCreate(request, out var options, out var error));
        Assert.Null(error);
        Assert.Equal(new DemoSeedOptions(new(25, 40), new(50, 100), new(1, 2)), options);
    }

    [Fact]
    public void HighLevelSelectsMaximumCatalogSize()
    {
        var request = new DemoSeedRequest { Categories = "high", Products = "high", Variants = "high" };

        Assert.True(DemoSeedOptions.TryCreate(request, out var options, out var error));
        Assert.Null(error);
        Assert.Equal(new DemoSeedOptions(new(50, 80), new(400, 500), new(5, 6)), options);
    }

    [Fact]
    public void DrawnAmountStaysInsideInclusiveRange()
    {
        var range = new DemoSeedRange(3, 5);

        for (var attempt = 0; attempt < 100; attempt++)
        {
            Assert.InRange(range.Pick(), 3, 5);
        }
    }

    [Theory]
    [InlineData("categories")]
    [InlineData("products")]
    [InlineData("variants")]
    public void UnsupportedLevelIsRejected(string field)
    {
        var request = new DemoSeedRequest
        {
            Categories = field == "categories" ? "huge" : null,
            Products = field == "products" ? "huge" : null,
            Variants = field == "variants" ? "huge" : null
        };

        Assert.False(DemoSeedOptions.TryCreate(request, out var options, out var error));
        Assert.Null(options);
        Assert.Contains(field, error);
    }
}
