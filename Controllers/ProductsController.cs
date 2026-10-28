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
        [FromQuery] string? q, [FromQuery] string? sort, CancellationToken cancellationToken)
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
            return BadRequest(new { message = "sort musi mieć wartość name-asc, name-desc, newest albo oldest." });
        }

        try
        {
            return Ok(await BuildListAsync(database.Products, page, pageSize, q, [], sort, cancellationToken));
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
        CancellationToken cancellationToken)
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
            return BadRequest(new { message = "sort musi mieć wartość name-asc, name-desc, newest albo oldest." });
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
            return Ok(await BuildListAsync(scopedProducts, page, pageSize, q, [], sort, cancellationToken));
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
            return BadRequest(new { message = "sort musi mieć wartość name-asc, name-desc, newest albo oldest." });
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
            IQueryable<Product> scope = database.Products;
            if (request.CategoryId.HasValue || request.CategorySlug is not null)
            {
                var (branchIds, error) = await ResolveCategoryBranchAsync(request.CategoryId,
                    request.CategorySlug, cancellationToken);
                if (error is not null)
                {
                    return error;
                }

                var ids = branchIds!;
                scope = scope.Where(product => product.ProductCategories.Any(link => ids.Contains(link.CategoryId)));
            }

            return Ok(await BuildListAsync(scope, page, pageSize, request.Q, normalizedAttributes,
                request.Sort, cancellationToken));
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
        sort is not ("name-asc" or "name-desc" or "newest" or "oldest");

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

    private async Task<ProductListResponse> BuildListAsync(IQueryable<Product> scope,
        int? page, int? pageSize, string? q, (string Code, string Value)[] attributes, string? sort,
        CancellationToken cancellationToken)
    {
        var paginated = page.HasValue || pageSize.HasValue;
        var selectedPage = page ?? 1;
        var selectedPageSize = pageSize ?? 20;
        var now = DateTime.UtcNow;
        IQueryable<Product> query = scope
            .AsNoTracking()
            .AsSplitQuery()
            .Where(product => product.Status == CatalogStatus.Published &&
                product.ProductCategories.Any() &&
                product.Variants.Any(variant => variant.Status == CatalogStatus.Published &&
                    variant.PriceChanges.Any(price => price.EffectiveAt <= now)));

        if (attributes.Length > 0)
        {
            IQueryable<ProductVariant> matchingVariants = database.ProductVariants
                .Where(variant => variant.Status == CatalogStatus.Published &&
                    variant.PriceChanges.Any(price => price.EffectiveAt <= now));

            foreach (var group in attributes.GroupBy(attribute => attribute.Code))
            {
                var code = group.Key;
                var values = group.Select(attribute => attribute.Value).ToArray();
                matchingVariants = matchingVariants.Where(variant => variant.VariantAttributePairs.Any(link =>
                    link.AttributePair.AttributeKey.Code == code && values.Contains(link.AttributePair.Value)));
            }

            query = query.Where(product => matchingVariants.Any(variant => variant.ProductId == product.Id));

            if (!string.IsNullOrWhiteSpace(q))
            {
                var pattern = SearchPattern(q.Trim());
                query = query.Where(product => EF.Functions.ILike(product.Name, pattern, @"\") ||
                    matchingVariants.Any(variant => variant.ProductId == product.Id &&
                        EF.Functions.ILike(variant.SKU, pattern, @"\")));
            }
        }
        else if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = SearchPattern(q.Trim());
            query = query.Where(product => EF.Functions.ILike(product.Name, pattern, @"\") ||
                product.Variants.Any(variant => variant.Status == CatalogStatus.Published &&
                    variant.PriceChanges.Any(price => price.EffectiveAt <= now) &&
                    EF.Functions.ILike(variant.SKU, pattern, @"\")));
        }

        query = sort switch
        {
            "name-desc" => query.OrderByDescending(product => product.Name).ThenBy(product => product.Id),
            "newest" => query.OrderByDescending(product => product.PublishedAt).ThenBy(product => product.Id),
            "oldest" => query.OrderBy(product => product.PublishedAt).ThenBy(product => product.Id),
            _ => query.OrderBy(product => product.Name).ThenBy(product => product.Id)
        };

        int? totalCount = null;
        if (paginated)
        {
            totalCount = await query.CountAsync(cancellationToken);
            query = query.Skip((page.GetValueOrDefault(1) - 1) * selectedPageSize).Take(selectedPageSize);
        }

        var products = await query.Select(product => new
            {
                product.Id, product.Name, product.Slug
            }).ToListAsync(cancellationToken);
        var productIds = products.Select(product => product.Id).ToArray();
        var variants = await database.ProductVariants.AsNoTracking()
            .Where(variant => productIds.Contains(variant.ProductId) &&
                variant.Status == CatalogStatus.Published &&
                variant.PriceChanges.Any(price => price.EffectiveAt <= now))
            .Select(variant => new { variant.Id, variant.ProductId, variant.SKU, variant.ImageUrls })
            .ToListAsync(cancellationToken);
        var variantIds = variants.Select(variant => variant.Id).ToArray();
        var prices = await database.VariantPriceChanges.AsNoTracking()
            .Where(price => variantIds.Contains(price.VariantId) && price.EffectiveAt <= now &&
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
