using System.Text.Json;
using E_commerce_backend.Contracts.CatalogManagement;
using E_commerce_backend.Services.Catalog.Management;

namespace E_commerce_backend.Tests.Unit;

public sealed class CatalogManagementInputTests
{
    [Theory]
    [InlineData("Żółta łódź — 42!", "zolta-lodz-42")]
    [InlineData("  Foo___Bar  ", "foo-bar")]
    public void SlugsTransliteratePolishAndSeparateWords(string input, string expected) => Assert.Equal(expected, CatalogInput.GenerateSlug(input));
    [Fact]
    public void CanonicalValuesPreserveCaseAndInternalSpacing()
    {
        Assert.Equal("Żółty", CatalogInput.Canonical("\u00a0Z\u0307o\u0301łty\u3000"));
        Assert.NotEqual(CatalogInput.Canonical("S"), CatalogInput.Canonical("s"));
        Assert.Equal("a  b", CatalogInput.Canonical(" a  b "));
        Assert.Throws<CatalogValidation>(() => CatalogInput.Slug(null, "你好"));
    }
    [Theory]
    [InlineData("http://example.com/a.png")]
    [InlineData("https://user:password@example.com/a.png")]
    [InlineData("/local.png")]
    public void UnsafeImageMetadataIsRejected(string url) =>
        Assert.Throws<CatalogValidation>(() => CatalogInput.Variant(new("SKU", null, null, [url], [])));
    [Fact]
    public void RequestsRejectServerFieldsAndStockRequiresExplicitVersion()
    {
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ProductCreateRequest>("{\"name\":\"X\",\"status\":\"Published\"}", json));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<VariantEditRequest>("{\"productId\":\"00000000-0000-0000-0000-000000000001\"}", json));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<StockSetRequest>("{\"quantityOnHand\":2,\"reason\":\"X\"}", json));
        Assert.Null(JsonSerializer.Deserialize<StockSetRequest>("{\"quantityOnHand\":2,\"expectedVersion\":null,\"reason\":\"X\"}", json)!.ExpectedVersion);
    }
}
