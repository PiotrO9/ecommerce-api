using E_commerce_backend.Contracts.Catalog;
using E_commerce_backend.Data;
using E_commerce_backend.Entities;
using E_commerce_backend.Services.Catalog;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace E_commerce_backend.Controllers;

public sealed class ProductsController(AppDbContext database) : WebApiControllerBase
{
    [HttpGet("catalog")]
    public async Task<IActionResult> GetCatalog([FromQuery] int? page, [FromQuery] int? pageSize,
        [FromQuery] string? q, [FromQuery] string? sort, [FromQuery] decimal? minPrice,
        [FromQuery] decimal? maxPrice, CancellationToken cancellationToken)
    {
        if (HasAttributeFiltersInQuery())
        {
            return BadRequest(new { message = "Filtry cech przekaż w body POST /api/v1/web/products/search." });
        }

        if (InvalidPagination(page, pageSize))
        {
            return BadRequest(new { message = "page musi być dodatnie, a pageSize musi mieścić się w zakresie 1–100." });
        }

        if (InvalidSearch(q))
        {
            return BadRequest(new { message = "q może mieć maksymalnie 100 znaków po usunięciu skrajnych białych znaków." });
        }

        if (InvalidSort(sort))
        {
            return BadRequest(new { message = "sort musi mieć wartość name-asc, name-desc, newest, oldest, price-asc albo price-desc." });
        }

        if (InvalidPriceRange(minPrice, maxPrice))
        {
            return BadRequest(new { message = "Podaj nieujemne ceny PLN z dokładnością do 0,01 i minPrice <= maxPrice." });
        }

        try
        {
            return Ok(await BuildListAsync(null, page, pageSize, q, [], sort,
                minPrice, maxPrice,
                DateTime.UtcNow, cancellationToken));
        }
        catch (NpgsqlException)
        {
            return StatusCode(503, new { message = "Produkty są chwilowo niedostępne." });
        }
        catch (TimeoutException)
        {
            return StatusCode(503, new { message = "Produkty są chwilowo niedostępne." });
        }
    }

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] Guid? id, [FromQuery] string? slug,
        CancellationToken cancellationToken)
    {
        if (id.HasValue == (slug is not null) || (slug is not null && string.IsNullOrWhiteSpace(slug)))
        {
            return BadRequest(new { message = "Podaj dokładnie jeden parametr: id albo slug." });
        }

        try
        {
            var now = DateTime.UtcNow;
            var query = database.Products
                .AsNoTracking()
                .AsSplitQuery()
                .Where(product => product.Status == CatalogStatus.Published);

            query = id.HasValue
                ? query.Where(product => product.Id == id.Value)
                : query.Where(product => product.Slug == slug!.Trim().ToLowerInvariant());

            var product = await query
                .Include(product => product.ProductCategories)
                    .ThenInclude(link => link.Category)
                .Include(product => product.Variants)
                    .ThenInclude(variant => variant.Stock)
                .Include(product => product.Variants)
                    .ThenInclude(variant => variant.PriceChanges.Where(price => price.EffectiveAt <= now))
                .Include(product => product.Variants)
                    .ThenInclude(variant => variant.VariantAttributePairs)
                        .ThenInclude(link => link.AttributePair)
                            .ThenInclude(pair => pair.AttributeKey)
                .SingleOrDefaultAsync(cancellationToken);

            var response = ProductDetailsBuilder.Build(product, now);
            return response is null
                ? NotFound(new { message = "Nie znaleziono produktu." })
                : Ok(response);
        }
        catch (NpgsqlException)
        {
            return StatusCode(503, new { message = "Produkt jest chwilowo niedostępny." });
        }
        catch (TimeoutException)
        {
            return StatusCode(503, new { message = "Produkt jest chwilowo niedostępny." });
        }
    }

    [HttpGet("by-category")]
    public async Task<IActionResult> GetByCategory([FromQuery] Guid? categoryId, [FromQuery] string? slug,
        [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? q, [FromQuery] string? sort,
        [FromQuery] decimal? minPrice, [FromQuery] decimal? maxPrice, CancellationToken cancellationToken)
    {
        if (HasAttributeFiltersInQuery())
        {
            return BadRequest(new { message = "Filtry cech przekaż w body POST /api/v1/web/products/search." });
        }

        if (categoryId.HasValue == (slug is not null) || (slug is not null && string.IsNullOrWhiteSpace(slug)))
        {
            return BadRequest(new { message = "Podaj dokładnie jeden parametr: categoryId albo slug." });
        }

        if (InvalidPagination(page, pageSize))
        {
            return BadRequest(new { message = "page musi być dodatnie, a pageSize musi mieścić się w zakresie 1–100." });
        }

        if (InvalidSearch(q))
        {
            return BadRequest(new { message = "q może mieć maksymalnie 100 znaków po usunięciu skrajnych białych znaków." });
        }

        if (InvalidSort(sort))
        {
            return BadRequest(new { message = "sort musi mieć wartość name-asc, name-desc, newest, oldest, price-asc albo price-desc." });
        }

        if (InvalidPriceRange(minPrice, maxPrice))
        {
            return BadRequest(new { message = "Podaj nieujemne ceny PLN z dokładnością do 0,01 i minPrice <= maxPrice." });
        }

        try
        {
            var (branchIds, error) = await ResolveCategoryBranchAsync(categoryId, slug, cancellationToken);
            if (error is not null)
            {
                return error;
            }

            var ids = branchIds!;
            var scopedProducts = database.Products
                .Where(product => product.ProductCategories.Any(link => ids.Contains(link.CategoryId)));
            return Ok(await BuildListAsync(scopedProducts, page, pageSize, q, [], sort,
                minPrice, maxPrice,
                DateTime.UtcNow, cancellationToken));
        }
        catch (NpgsqlException)
        {
            return StatusCode(503, new { message = "Produkty są chwilowo niedostępne." });
        }
        catch (TimeoutException)
        {
            return StatusCode(503, new { message = "Produkty są chwilowo niedostępne." });
        }
    }

    [HttpPost("search")]
    public async Task<IActionResult> Search([FromBody] ProductSearchRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new { message = "Podaj body wyszukiwania." });
        }

        var page = request.Page ?? 1;
        var pageSize = request.PageSize ?? 20;
        if (InvalidPagination(page, pageSize))
        {
            return BadRequest(new { message = "page musi być dodatnie, a pageSize musi mieścić się w zakresie 1–100." });
        }

        if (InvalidSearch(request.Q))
        {
            return BadRequest(new { message = "q może mieć maksymalnie 100 znaków po usunięciu skrajnych białych znaków." });
        }

        if (InvalidSort(request.Sort))
        {
            return BadRequest(new { message = "sort musi mieć wartość name-asc, name-desc, newest, oldest, price-asc albo price-desc." });
        }

        if (InvalidPriceRange(request.MinPrice, request.MaxPrice))
        {
            return BadRequest(new { message = "Podaj nieujemne ceny PLN z dokładnością do 0,01 i minPrice <= maxPrice." });
        }

        if (request.CategoryId.HasValue && request.CategorySlug is not null ||
            request.CategorySlug is not null && string.IsNullOrWhiteSpace(request.CategorySlug))
        {
            return BadRequest(new { message = "Podaj categoryId albo categorySlug, nie oba naraz; categorySlug nie może być pusty." });
        }

        if (!TryNormalizeAttributes(request.Attributes, out var normalizedAttributes))
        {
            return BadRequest(new { message = "Podaj maksymalnie 10 cech i 30 wartości: niepusty code (do 100 znaków) i niepuste values (każda do 255 znaków)." });
        }

        try
        {
            IQueryable<Product>? scope = null;
            if (request.CategoryId.HasValue || request.CategorySlug is not null)
            {
                var (branchIds, error) = await ResolveCategoryBranchAsync(request.CategoryId,
                    request.CategorySlug, cancellationToken);
                if (error is not null)
                {
                    return error;
                }

                var ids = branchIds!;
                scope = database.Products.Where(product => product.ProductCategories.Any(link =>
                    ids.Contains(link.CategoryId)));
            }

            var now = DateTime.UtcNow;
            var list = await BuildListAsync(scope, page, pageSize, request.Q, normalizedAttributes,
                request.Sort, request.MinPrice, request.MaxPrice, now, cancellationToken);
            var filters = await BuildAvailableFiltersAsync(scope, request.Q, normalizedAttributes,
                request.MinPrice, request.MaxPrice, now, cancellationToken);
            return Ok(new ProductSearchResponse(list.Items, list.Pagination, filters));
        }
        catch (NpgsqlException)
        {
            return StatusCode(503, new { message = "Produkty są chwilowo niedostępne." });
        }
        catch (TimeoutException)
        {
            return StatusCode(503, new { message = "Produkty są chwilowo niedostępne." });
        }
    }

    [HttpGet("filter-options")]
    public async Task<IActionResult> GetFilterOptions([FromQuery] Guid? categoryId,
        [FromQuery] string? slug, CancellationToken cancellationToken)
    {
        if (categoryId.HasValue && slug is not null ||
            slug is not null && string.IsNullOrWhiteSpace(slug))
        {
            return BadRequest(new { message = "Podaj categoryId albo slug, nie oba naraz; slug nie może być pusty." });
        }

        try
        {
            Guid[]? branchIds = null;
            if (categoryId.HasValue || slug is not null)
            {
                var (ids, error) = await ResolveCategoryBranchAsync(categoryId, slug, cancellationToken);
                if (error is not null)
                {
                    return error;
                }

                branchIds = ids;
            }

            var now = DateTime.UtcNow;
            var links = database.VariantAttributePairs.AsNoTracking()
                .Where(link => link.Variant.Status == CatalogStatus.Published &&
                    link.Variant.Product.Status == CatalogStatus.Published &&
                    link.Variant.Product.ProductCategories.Any() &&
                    link.Variant.PriceChanges.Any(price => price.EffectiveAt <= now));

            if (branchIds is not null)
            {
                var ids = branchIds;
                links = links.Where(link => link.Variant.Product.ProductCategories.Any(
                    category => ids.Contains(category.CategoryId)));
            }

            var pairs = await links.Select(link => new
                {
                    link.AttributePair.AttributeKey.Code,
                    link.AttributePair.AttributeKey.DisplayName,
                    link.AttributePair.Value
                })
                .Distinct()
                .ToListAsync(cancellationToken);

            var options = pairs.GroupBy(pair => new { pair.Code, pair.DisplayName })
                .OrderBy(group => group.Key.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(group => group.Key.Code, StringComparer.Ordinal)
                .Select(group => new ProductFilterOptionResponse(group.Key.Code,
                    group.Key.DisplayName,
                    group.Select(pair => pair.Value).OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                        .ToArray()))
                .ToArray();
            return Ok(options);
        }
        catch (NpgsqlException)
        {
            return StatusCode(503, new { message = "Filtry są chwilowo niedostępne." });
        }
        catch (TimeoutException)
        {
            return StatusCode(503, new { message = "Filtry są chwilowo niedostępne." });
        }
    }

    private async Task<(Guid[]? BranchIds, IActionResult? Error)> ResolveCategoryBranchAsync(
        Guid? categoryId, string? slug, CancellationToken cancellationToken)
    {
        var categories = await database.Categories.AsNoTracking().ToListAsync(cancellationToken);
        if (slug is not null)
        {
            var normalizedSlug = slug.Trim().ToLowerInvariant();
            var matches = categories.Where(category => category.Slug == normalizedSlug).Take(2).ToArray();
            if (matches.Length > 1)
            {
                return (null, Conflict(new { message = "Slug wskazuje kilka kategorii. Użyj categoryId." }));
            }

            categoryId = matches.FirstOrDefault()?.Id;
        }

        if (categoryId is not Guid selectedId)
        {
            return (null, NotFound(new { message = "Nie znaleziono kategorii." }));
        }

        var branchIds = CategoryTreeBuilder.GetBranchIds(categories, selectedId);
        return branchIds is null
            ? (null, NotFound(new { message = "Nie znaleziono kategorii." }))
            : (branchIds.ToArray(), null);
    }

    private static bool InvalidPagination(int? page, int? pageSize)
    {
        if (!page.HasValue && !pageSize.HasValue)
        {
            return false;
        }

        var selectedPage = page ?? 1;
        var selectedPageSize = pageSize ?? 20;
        var offset = ((long)selectedPage - 1) * selectedPageSize;
        return selectedPage < 1 || selectedPageSize is < 1 or > 100 || offset > int.MaxValue;
    }

    private static bool InvalidSearch(string? q) => q?.Trim().Length > 100;

    private static bool InvalidSort(string? sort) => sort is not null &&
        sort is not ("name-asc" or "name-desc" or "newest" or "oldest" or "price-asc" or "price-desc");

    private static bool InvalidPriceRange(decimal? minPrice, decimal? maxPrice)
    {
        const decimal maxAmount = 9999999999999999.99m;
        return (minPrice is decimal min && (min < 0 || min > maxAmount || decimal.Round(min, 2) != min)) ||
            (maxPrice is decimal max && (max < 0 || max > maxAmount || decimal.Round(max, 2) != max)) ||
            (minPrice.HasValue && maxPrice.HasValue && minPrice > maxPrice);
    }

    private bool HasAttributeFiltersInQuery() => HttpContext?.Request.Query.Keys.Any(key =>
        key.StartsWith("attributes", StringComparison.OrdinalIgnoreCase)) == true;

    private static bool TryNormalizeAttributes(List<ProductAttributeFilterRequest>? attributes,
        out (string Code, string Value)[] normalized)
    {
        normalized = [];
        if (attributes is null)
        {
            return true;
        }

        if (attributes.Count > 10)
        {
            return false;
        }

        var result = new List<(string Code, string Value)>();
        foreach (var attribute in attributes)
        {
            var code = attribute?.Code?.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(code) || code.Length > 100 ||
                attribute?.Values is not { Count: > 0 } values || result.Count + values.Count > 30)
            {
                return false;
            }

            foreach (var rawValue in values)
            {
                var value = rawValue?.Trim();
                if (string.IsNullOrEmpty(value) || value.Length > 255)
                {
                    return false;
                }

                result.Add((code, value));
            }
        }

        normalized = result.Distinct().ToArray();
        return true;
    }

    private static string SearchPattern(string q) => "%" + q
        .Replace(@"\", @"\\")
        .Replace("%", @"\%")
        .Replace("_", @"\_") + "%";

    private IQueryable<VariantPriceChange> CurrentPlnPrices(DateTime now) =>
        database.VariantPriceChanges.FromSqlInterpolated($"""
            SELECT DISTINCT ON (variant_id)
                id, variant_id, base_amount, selling_amount, currency, effective_at, created_at, reason
            FROM app.variant_price_changes
            WHERE currency = 'PLN' AND effective_at <= {now}
            ORDER BY variant_id, effective_at DESC, created_at DESC, id DESC
            """).AsNoTracking();

    private IQueryable<ProductVariant> MatchingVariants(IQueryable<Product>? scope, DateTime now,
        string? q, (string Code, string Value)[] attributes, decimal? minPrice, decimal? maxPrice)
    {
        IQueryable<ProductVariant> variants = database.ProductVariants.AsNoTracking()
            .Where(variant => variant.Status == CatalogStatus.Published &&
                variant.Product.Status == CatalogStatus.Published &&
                variant.Product.ProductCategories.Any() &&
                variant.PriceChanges.Any(price => price.EffectiveAt <= now));

        if (scope is not null)
        {
            variants = variants.Where(variant => scope.Any(product => product.Id == variant.ProductId));
        }

        if (minPrice.HasValue || maxPrice.HasValue)
        {
            var currentPrices = CurrentPlnPrices(now);
            variants = variants.Where(variant => currentPrices.Any(price =>
                price.VariantId == variant.Id &&
                (!minPrice.HasValue || price.SellingAmount >= minPrice.Value) &&
                (!maxPrice.HasValue || price.SellingAmount <= maxPrice.Value)));
        }

        foreach (var group in attributes.GroupBy(attribute => attribute.Code))
        {
            var code = group.Key;
            var values = group.Select(attribute => attribute.Value).ToArray();
            variants = variants.Where(variant => variant.VariantAttributePairs.Any(link =>
                link.AttributePair.AttributeKey.Code == code && values.Contains(link.AttributePair.Value)));
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = SearchPattern(q.Trim());
            variants = variants.Where(variant =>
                EF.Functions.ILike(variant.Product.Name, pattern, @"\") ||
                EF.Functions.ILike(variant.SKU, pattern, @"\"));
        }

        return variants;
    }

    private async Task<ProductSearchFilterResponse[]> BuildAvailableFiltersAsync(
        IQueryable<Product>? scope, string? q, (string Code, string Value)[] attributes,
        decimal? minPrice, decimal? maxPrice, DateTime now, CancellationToken cancellationToken)
    {
        var baseVariants = MatchingVariants(scope, now, q, [], minPrice, maxPrice);
        var keys = await database.VariantAttributePairs.AsNoTracking()
            .Where(link => baseVariants.Any(variant => variant.Id == link.VariantId))
            .Select(link => new FacetKeyRow(link.AttributePair.AttributeKey.Code,
                link.AttributePair.AttributeKey.DisplayName))
            .Distinct()
            .ToListAsync(cancellationToken);

        var rows = await CountFacetRowsAsync(baseVariants, attributes, cancellationToken);

        return ProductFacetResponseBuilder.Build(
            keys.Select(key => (key.Code, key.DisplayName)).ToArray(),
            rows.Select(row => (row.Code, row.Value, row.Count)).ToArray(), attributes);
    }

    private async Task<List<FacetCountRow>> CountFacetRowsAsync(
        IQueryable<ProductVariant> baseVariants, (string Code, string Value)[] attributes,
        CancellationToken cancellationToken)
    {
        var links = database.VariantAttributePairs.AsNoTracking()
            .Where(link => baseVariants.Any(variant => variant.Id == link.VariantId));
        foreach (var group in attributes.GroupBy(attribute => attribute.Code))
        {
            var code = group.Key;
            var values = group.Select(attribute => attribute.Value).ToArray();
            links = links.Where(link => link.AttributePair.AttributeKey.Code == code ||
                link.Variant.VariantAttributePairs.Any(other =>
                    other.AttributePair.AttributeKey.Code == code &&
                    values.Contains(other.AttributePair.Value)));
        }

        return await links.Select(link => new
            {
                link.AttributePair.AttributeKey.Code,
                link.AttributePair.Value,
                link.Variant.ProductId
            })
            .Distinct()
            .GroupBy(item => new { item.Code, item.Value })
            .Select(group => new FacetCountRow(group.Key.Code, group.Key.Value, group.Count()))
            .ToListAsync(cancellationToken);
    }

    private sealed record FacetKeyRow(string Code, string DisplayName);

    private sealed record FacetCountRow(string Code, string Value, int Count);

    private async Task<ProductListResponse> BuildListAsync(IQueryable<Product>? scope,
        int? page, int? pageSize, string? q, (string Code, string Value)[] attributes, string? sort,
        decimal? minPrice, decimal? maxPrice, DateTime now, CancellationToken cancellationToken)
    {
        var paginated = page.HasValue || pageSize.HasValue;
        var selectedPage = page ?? 1;
        var selectedPageSize = pageSize ?? 20;
        var matchingVariants = MatchingVariants(scope, now, q, attributes, minPrice, maxPrice);
        var productScope = scope ?? database.Products;
        IQueryable<Product> query = productScope
            .AsNoTracking()
            .AsSplitQuery()
            .Where(product => product.Status == CatalogStatus.Published &&
                product.ProductCategories.Any() &&
                matchingVariants.Any(variant => variant.ProductId == product.Id));

        var currentPlnPrices = CurrentPlnPrices(now);
        var pricesByProduct = matchingVariants
            .GroupJoin(currentPlnPrices, variant => variant.Id, price => price.VariantId,
                (variant, prices) => new { variant.ProductId, prices })
            .SelectMany(row => row.prices.DefaultIfEmpty(),
                (row, price) => new { row.ProductId, Amount = (decimal?)price!.SellingAmount })
            .GroupBy(row => row.ProductId)
            .Select(group => new { ProductId = group.Key, Amount = group.Min(row => row.Amount) });

        int? totalCount = null;
        if (paginated)
        {
            totalCount = await query.CountAsync(cancellationToken);
        }

        query = sort switch
        {
            "name-desc" => query.OrderByDescending(product => product.Name).ThenBy(product => product.Id),
            "newest" => query.OrderByDescending(product => product.PublishedAt).ThenBy(product => product.Id),
            "oldest" => query.OrderBy(product => product.PublishedAt).ThenBy(product => product.Id),
            "price-asc" =>
                from product in productScope.AsNoTracking()
                join price in pricesByProduct on product.Id equals price.ProductId
                where product.Status == CatalogStatus.Published && product.ProductCategories.Any()
                orderby price.Amount == null, price.Amount, product.Id
                select product,
            "price-desc" =>
                from product in productScope.AsNoTracking()
                join price in pricesByProduct on product.Id equals price.ProductId
                where product.Status == CatalogStatus.Published && product.ProductCategories.Any()
                orderby price.Amount == null, price.Amount descending, product.Id
                select product,
            _ => query.OrderBy(product => product.Name).ThenBy(product => product.Id)
        };

        if (paginated)
        {
            query = query.Skip((page.GetValueOrDefault(1) - 1) * selectedPageSize).Take(selectedPageSize);
        }

        var products = await query.Select(product => new
            {
                product.Id, product.Name, product.Slug
            }).ToListAsync(cancellationToken);
        var productIds = products.Select(product => product.Id).ToArray();
        var matchingVariantIds = matchingVariants.Where(variant => productIds.Contains(variant.ProductId))
            .Select(variant => variant.Id);
        var variants = await database.ProductVariants.AsNoTracking()
            .Where(variant => matchingVariantIds.Contains(variant.Id))
            .Select(variant => new { variant.Id, variant.ProductId, variant.SKU, variant.ImageUrls })
            .ToListAsync(cancellationToken);
        var variantIds = variants.Select(variant => variant.Id).ToArray();
        var prices = await database.VariantPriceChanges.AsNoTracking()
            .Where(price => variantIds.Contains(price.VariantId) && price.EffectiveAt <= now &&
                (minPrice == null && maxPrice == null || price.Currency == "PLN") &&
                price.Id == database.VariantPriceChanges
                    .Where(candidate => candidate.VariantId == price.VariantId &&
                        candidate.Currency == price.Currency && candidate.EffectiveAt <= now)
                    .OrderByDescending(candidate => candidate.EffectiveAt)
                    .ThenByDescending(candidate => candidate.CreatedAt)
                    .ThenByDescending(candidate => candidate.Id)
                    .Select(candidate => candidate.Id).First())
            .Select(price => new { price.VariantId, price.Currency, price.SellingAmount })
            .ToListAsync(cancellationToken);
        var variantsByProduct = variants.GroupBy(variant => variant.ProductId)
            .ToDictionary(group => group.Key, group => group.OrderBy(variant => variant.SKU,
                StringComparer.Ordinal).ToArray());
        var pricesByVariant = prices.GroupBy(price => price.VariantId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var items = products.Select(product =>
        {
            var productVariants = variantsByProduct.GetValueOrDefault(product.Id) ?? [];
            var thumbnail = productVariants.SelectMany(variant => variant.ImageUrls).FirstOrDefault();
            var pricesFrom = productVariants
                .SelectMany(variant => pricesByVariant.GetValueOrDefault(variant.Id) ?? [])
                .GroupBy(price => price.Currency, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => new ProductVariantPriceResponse(group.Min(price => price.SellingAmount),
                    group.Key)).ToArray();
            return new ProductListItemResponse(product.Id, product.Name, product.Slug,
                thumbnail, pricesFrom);
        }).ToArray();

        ProductListPaginationResponse? pagination = null;
        if (totalCount is int count)
        {
            var totalPages = (int)(((long)count + selectedPageSize - 1) / selectedPageSize);
            pagination = new ProductListPaginationResponse(
                selectedPage, selectedPageSize, count, totalPages,
                selectedPage > 1, selectedPage < totalPages);
        }

        return new ProductListResponse(items, pagination);
    }
}
