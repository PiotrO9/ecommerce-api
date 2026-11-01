using E_commerce_backend.Entities;
using E_commerce_backend.Services.Pricing;

namespace E_commerce_backend.Tests.Unit;

public sealed class PromotionCalculatorTests
{
    [Theory]
    [InlineData("percentage", 10, 100, 90)]
    [InlineData("percentage", 100, 100, 0)]
    [InlineData("amount", 5, 100, 95)]
    [InlineData("amount", 100, 100, 0)]
    public void CalculatesRule(string type, int value, int basis, int expected) =>
        Assert.Equal(expected, PromotionCalculator.Calculate(basis, type, value));

    [Fact]
    public void RoundsFinalPriceAndRejectsInvalidRules()
    {
        Assert.Equal(0.03m, PromotionCalculator.Calculate(0.05m, "percentage", 50));
        Assert.Null(PromotionCalculator.Calculate(10, "percentage", 100.01m));
        Assert.Null(PromotionCalculator.Calculate(10, "amount", 10.01m));
        Assert.Null(PromotionCalculator.Calculate(10, "amount", 0));
        Assert.Null(PromotionCalculator.Calculate(10, "amount", 1.001m));
        Assert.Null(PromotionCalculator.Calculate(10, "unknown", 1));
    }

    private static readonly DateTime Now = new(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc);
    private static VariantPriceChange Price(int days, decimal value) => new() { VariantId = Guid.Empty,
        BaseAmount = value, SellingAmount = value, EffectiveAt = Now.AddDays(days), CreatedAt = Now.AddDays(days), Reason = "Test" };
    private static VariantOfferEvent Event(int days, bool visible = true, bool complete = true) => new()
        { OccurredAt = Now.AddDays(days), Visible = visible, CompleteFromBirth = complete };

    [Fact]
    public void IncludesPriceBeforeWindowAndPreviousDiscountButNotFuture()
    {
        var result = PriorPriceCalculator.Calculate([Price(-40, 100), Price(-5, 80), Price(1, 10)], [Event(-40)], Now)!;
        Assert.Equal(80, result.Amount);
        Assert.Equal("Full30Days", result.Basis);
        var unchanged = PriorPriceCalculator.Calculate([Price(-40, 100)], [Event(-40)], Now)!;
        Assert.Equal(100, unchanged.Amount);
        Assert.Equal(PriorPriceCalculator.WindowStart(Now), unchanged.ReferenceStart);
    }

    [Fact]
    public void SeparatesNewShortOfferFromUnknownImportedHistory()
    {
        Assert.Equal("SinceFirstOffer", PriorPriceCalculator.Calculate([Price(-40, 100)], [Event(-5)], Now)!.Basis);
        Assert.Null(PriorPriceCalculator.Calculate([Price(-40, 100)], [Event(-5, complete: false)], Now));
        Assert.NotNull(PriorPriceCalculator.Calculate([Price(-40, 100)], [Event(-40, complete: false)], Now));
        Assert.Null(PriorPriceCalculator.Calculate([Price(0, 100)], [Event(-5)], Now));
    }

    [Fact]
    public void ExcludesPricesAppliedOnlyWhileHidden()
    {
        var result = PriorPriceCalculator.Calculate([Price(-40, 100), Price(-19, 10), Price(-11, 110)],
            [Event(-40), Event(-20, false), Event(-10)], Now)!;
        Assert.Equal(100, result.Amount);
    }

    [Fact]
    public void CalendarWindowUsesWarsawOffsetAcrossDst()
    {
        var spring = new DateTime(2026, 4, 15, 10, 0, 0, DateTimeKind.Utc);
        Assert.Equal(new DateTime(2026, 3, 16, 11, 0, 0, DateTimeKind.Utc), PriorPriceCalculator.WindowStart(spring));
        Assert.Equal(new DateTime(2026, 10, 1, 11, 0, 0, DateTimeKind.Utc), PriorPriceCalculator.WindowStart(Now));
    }
}
