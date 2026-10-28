using E_commerce_backend.Services.Catalog;

namespace E_commerce_backend.Tests.Unit;

public sealed class ProductFacetResponseBuilderTests
{
    [Fact]
    public void RetainsSelectedZeroCountValuesAndOffersAlternatives()
    {
        var filters = ProductFacetResponseBuilder.Build(
            [("demo-rozmiar", "Rozmiar"), ("demo-kolor", "Kolor"), ("pusty", "Pusty")],
            [("demo-kolor", "niebieski", 1), ("demo-rozmiar", "S", 1)],
            [("demo-kolor", "czerwony"), ("demo-rozmiar", "M"), ("nieznany", "wartosc")]);

        Assert.Equal(["demo-kolor", "nieznany", "demo-rozmiar"],
            filters.Select(filter => filter.Code));
        Assert.Equal("Kolor", filters[0].DisplayName);
        Assert.Equal(("czerwony", 0, true),
            (filters[0].Values[0].Value, filters[0].Values[0].Count, filters[0].Values[0].Selected));
        Assert.Equal(("niebieski", 1, false),
            (filters[0].Values[1].Value, filters[0].Values[1].Count, filters[0].Values[1].Selected));
        Assert.Equal("nieznany", filters[1].DisplayName);
        Assert.Equal(0, Assert.Single(filters[1].Values).Count);
    }

    [Fact]
    public void SelectedValueKeepsItsCountWithoutBeingRepeated()
    {
        var filters = ProductFacetResponseBuilder.Build(
            [("kolor", "Kolor")],
            [("kolor", "czerwony", 3), ("kolor", "niebieski", 2)],
            [("kolor", "czerwony"), ("kolor", "czerwony"), ("kolor", "niebieski")]);

        var values = Assert.Single(filters).Values;
        Assert.Equal(2, values.Count);
        Assert.All(values, value => Assert.True(value.Selected));
        Assert.Equal(3, values.Single(value => value.Value == "czerwony").Count);
        Assert.Equal(2, values.Single(value => value.Value == "niebieski").Count);
    }
}
