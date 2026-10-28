using E_commerce_backend.Data;
using E_commerce_backend.Entities;
using Microsoft.EntityFrameworkCore;

namespace E_commerce_backend.Services.Development;

public sealed record DemoSeedCreated(
    int Categories,
    int Products,
    int Variants,
    int AttributeKeys,
    int AttributePairs,
    int ProductCategoryLinks,
    int VariantAttributeLinks,
    int Stocks,
    int PriceChanges);

public sealed record DemoCatalogSeedResult(int Added, DemoSeedCreated Created);

public sealed class DemoCatalogSeeder(AppDbContext database)
{
    public async Task<DemoCatalogSeedResult> SeedAsync(DemoSeedOptions options, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);

        // Serializuje dwa równoczesne wywołania seedera na tej samej bazie.
        await database.Database.ExecuteSqlRawAsync(
            "SELECT pg_advisory_xact_lock(20261022, 2)", cancellationToken);

        // Jawny zakres bez CASCADE chroni przyszłe tabele zależne oraz profile i auth.
        // TRUNCATE omija wyzwalacz DELETE historii cen; endpoint działa tylko w Development.
        await database.Database.ExecuteSqlRawAsync("""
            TRUNCATE TABLE
                app.variant_price_changes,
                app.variant_stock,
                app.variant_attribute_pairs,
                app.product_categories,
                app.product_variants,
                app.products,
                app.attribute_pairs,
                app.attribute_keys,
                app.categories
            RESTRICT
            """, cancellationToken);

        var data = DemoCatalogSeedData.Create(options, DateTime.UtcNow);
        database.AddRange(data.Entities);
        await database.SaveChangesAsync(cancellationToken);

        // Publikacja następuje dopiero po zapisaniu kategorii, wariantów i cen.
        foreach (var variant in data.Variants)
        {
            variant.Status = CatalogStatus.Published;
            variant.PublishedAt = data.CreatedAt;
        }
        foreach (var product in data.Products)
        {
            product.Status = CatalogStatus.Published;
            product.PublishedAt = data.CreatedAt;
        }
        await database.SaveChangesAsync(cancellationToken);
        // Po dużym TRUNCATE i zapisie planista potrzebuje statystyk nowego katalogu.
        await database.Database.ExecuteSqlRawAsync("""
            ANALYZE app.categories, app.products, app.product_categories,
                app.product_variants, app.attribute_keys, app.attribute_pairs,
                app.variant_attribute_pairs, app.variant_stock, app.variant_price_changes
            """, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var created = data.Created;
        var added = created.Categories + created.Products + created.Variants + created.AttributeKeys
            + created.AttributePairs + created.ProductCategoryLinks + created.VariantAttributeLinks
            + created.Stocks + created.PriceChanges;
        return new DemoCatalogSeedResult(added, created);
    }
}

// Wszystkie klucze UUID powstają w aplikacji, więc cały graf można przygotować
// bez zapytań o istniejące rekordy po wykonaniu TRUNCATE.
public sealed class DemoCatalogSeedData
{
    private readonly List<object> _entities = [];
    private readonly List<Product> _products = [];
    private readonly List<ProductVariant> _variants = [];

    private DemoCatalogSeedData(DateTime createdAt) => CreatedAt = createdAt;

    public DateTime CreatedAt { get; }
    public IReadOnlyList<object> Entities => _entities;
    public IReadOnlyList<Product> Products => _products;
    public IReadOnlyList<ProductVariant> Variants => _variants;

    public DemoSeedCreated Created => new(
        _entities.OfType<Category>().Count(),
        _products.Count,
        _variants.Count,
        _entities.OfType<AttributeKey>().Count(),
        _entities.OfType<AttributePair>().Count(),
        _entities.OfType<ProductCategory>().Count(),
        _entities.OfType<VariantAttributePair>().Count(),
        _entities.OfType<VariantStock>().Count(),
        _entities.OfType<VariantPriceChange>().Count());

    public static DemoCatalogSeedData Create(DemoSeedOptions options, DateTime createdAt)
    {
        var data = new DemoCatalogSeedData(createdAt);
        var categoryTarget = options.Categories.Pick();
        var productTarget = options.Products.Pick();

        var clothing = data.Add(new Category { DisplayName = "Odzież demo", Slug = "demo-odziez" });
        var accessories = data.Add(new Category { DisplayName = "Akcesoria demo", Slug = "demo-akcesoria" });
        var shirts = data.Add(new Category
        {
            ParentId = clothing.Id, DisplayName = "Koszulki demo", Slug = "demo-koszulki"
        });

        var color = data.Add(new AttributeKey { Code = "demo-kolor", DisplayName = "Kolor" });
        var size = data.Add(new AttributeKey { Code = "demo-rozmiar", DisplayName = "Rozmiar" });
        var red = data.Add(new AttributePair { AttributeKeyId = color.Id, Value = "czerwony" });
        var blue = data.Add(new AttributePair { AttributeKeyId = color.Id, Value = "niebieski" });
        var small = data.Add(new AttributePair { AttributeKeyId = size.Id, Value = "S" });
        var medium = data.Add(new AttributePair { AttributeKeyId = size.Id, Value = "M" });

        var shirt = data.Add(new Product { Name = "Koszulka demo", Slug = "demo-koszulka" });
        var cap = data.Add(new Product { Name = "Czapka demo", Slug = "demo-czapka" });
        var redSmall = data.Add(new ProductVariant
        {
            ProductId = shirt.Id,
            SKU = "DEMO-TSHIRT-RED-S",
            ShortDescription = "Czerwona koszulka w rozmiarze S",
            Description = "Przykładowy wariant koszulki używany podczas rozwoju API."
        });
        var blueMedium = data.Add(new ProductVariant
        {
            ProductId = shirt.Id,
            SKU = "DEMO-TSHIRT-BLUE-M",
            ShortDescription = "Niebieska koszulka w rozmiarze M"
        });
        var oneCap = data.Add(new ProductVariant
        {
            ProductId = cap.Id, SKU = "DEMO-CAP-ONE", ShortDescription = "Czapka w jednej konfiguracji"
        });

        data.Add(new ProductCategory { ProductId = shirt.Id, CategoryId = shirts.Id });
        data.Add(new ProductCategory { ProductId = cap.Id, CategoryId = accessories.Id });
        data.AddVariantAttributes(redSmall.Id, red.Id, small.Id);
        data.AddVariantAttributes(blueMedium.Id, blue.Id, medium.Id);
        data.AddStock(redSmall.Id, 5);
        data.AddStock(blueMedium.Id, 8);
        data.AddStock(oneCap.Id, 3);
        data.AddPrice(redSmall.Id, "Demo: cena początkowa", 120m, 120m, createdAt.AddDays(-40));
        data.AddPrice(redSmall.Id, "Demo: obniżka", 120m, 90m, createdAt.AddDays(-5));
        data.AddPrice(blueMedium.Id, "Demo: cena początkowa", 110m, 110m, createdAt.AddDays(-10));
        data.AddPrice(oneCap.Id, "Demo: cena początkowa", 50m, 50m, createdAt.AddDays(-10));

        var categories = new List<Category> { shirts, accessories };
        for (var index = 4; index <= categoryTarget; index++)
        {
            var extra = data.Add(new Category
            {
                ParentId = clothing.Id,
                DisplayName = $"Kategoria demo {index:00}",
                Slug = $"demo-kategoria-{index:00}"
            });
            categories.Add(extra);
        }

        for (var productIndex = 3; productIndex <= productTarget; productIndex++)
        {
            var product = data.Add(new Product
            {
                Name = $"Produkt demo {productIndex:00}", Slug = $"demo-produkt-{productIndex:00}"
            });
            var category = categories[(productIndex - 3) % categories.Count];
            data.Add(new ProductCategory { ProductId = product.Id, CategoryId = category.Id });
        }

        var combinations = new List<(Guid Color, Guid Size)>
        {
            (red.Id, small.Id), (blue.Id, medium.Id),
            (red.Id, medium.Id), (blue.Id, small.Id)
        };
        if (options.VariantsPerProduct.Max > combinations.Count)
        {
            var large = data.Add(new AttributePair { AttributeKeyId = size.Id, Value = "L" });
            combinations.Add((red.Id, large.Id));
            combinations.Add((blue.Id, large.Id));
        }

        for (var productIndex = 1; productIndex <= data.Products.Count; productIndex++)
        {
            var product = data.Products[productIndex - 1];
            var existingBaseVariants = productIndex switch { 1 => 2, 2 => 1, _ => 0 };
            var variantTarget = options.VariantsPerProduct.Pick();
            for (var variantIndex = existingBaseVariants; variantIndex < variantTarget; variantIndex++)
            {
                var variantNumber = variantIndex + 1;
                var skuPrefix = productIndex switch
                {
                    1 => "DEMO-TSHIRT",
                    2 => "DEMO-CAP",
                    _ => $"DEMO-P{productIndex:00}"
                };
                var variant = data.Add(new ProductVariant
                {
                    ProductId = product.Id,
                    SKU = $"{skuPrefix}-V{variantNumber:00}",
                    ShortDescription = $"Wariant {variantNumber:00} produktu {product.Name}"
                });
                var combination = combinations[productIndex == 2 ? variantIndex - 1 : variantIndex];
                data.AddVariantAttributes(variant.Id, combination.Color, combination.Size);
                data.AddStock(variant.Id, 3 + variantNumber);
                var amount = 60m + productIndex * 5m + variantIndex * 2m;
                data.AddPrice(variant.Id, "Demo: cena początkowa", amount, amount,
                    createdAt.AddDays(-10));
            }
        }

        return data;
    }

    private T Add<T>(T entity) where T : class
    {
        _entities.Add(entity);
        if (entity is Product product) _products.Add(product);
        if (entity is ProductVariant variant) _variants.Add(variant);
        return entity;
    }

    private void AddVariantAttributes(Guid variantId, Guid colorId, Guid sizeId)
    {
        Add(new VariantAttributePair { VariantId = variantId, AttributePairId = colorId });
        Add(new VariantAttributePair { VariantId = variantId, AttributePairId = sizeId });
    }

    private void AddStock(Guid variantId, int quantity) =>
        Add(new VariantStock { VariantId = variantId, QuantityOnHand = quantity });

    private void AddPrice(Guid variantId, string reason, decimal baseAmount, decimal sellingAmount,
        DateTime effectiveAt) =>
        Add(new VariantPriceChange
        {
            VariantId = variantId,
            BaseAmount = baseAmount,
            SellingAmount = sellingAmount,
            EffectiveAt = effectiveAt,
            Reason = reason
        });
}
