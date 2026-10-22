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
        [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken)
    {
        if (categoryId.HasValue == (slug is not null) || (slug is not null && string.IsNullOrWhiteSpace(slug)))
        {
            return BadRequest(new { message = "Podaj dokładnie jeden parametr: categoryId albo slug." });
        }

        var paginated = page.HasValue || pageSize.HasValue;
        var selectedPage = page ?? 1;
        var selectedPageSize = pageSize ?? 20;
        var offset = ((long)selectedPage - 1) * selectedPageSize;
        if (paginated && (selectedPage < 1 || selectedPageSize is < 1 or > 100 || offset > int.MaxValue))
        {
            return BadRequest(new { message = "page musi być dodatnie, a pageSize musi mieścić się w zakresie 1–100." });
        }

        try
        {
            var categories = await database.Categories.AsNoTracking().ToListAsync(cancellationToken);
            if (slug is not null)
            {
                var normalizedSlug = slug.Trim().ToLowerInvariant();
                var matches = categories.Where(category => category.Slug == normalizedSlug).Take(2).ToArray();
                if (matches.Length > 1)
                {
                    return Conflict(new { message = "Slug wskazuje kilka kategorii. Użyj categoryId." });
                }

                categoryId = matches.FirstOrDefault()?.Id;
            }

            if (categoryId is not Guid selectedId)
            {
                return NotFound(new { message = "Nie znaleziono kategorii." });
            }

            var branchIds = CategoryTreeBuilder.GetBranchIds(categories, selectedId);
            if (branchIds is null)
            {
                return NotFound(new { message = "Nie znaleziono kategorii." });
            }

            var ids = branchIds.ToArray();
            var now = DateTime.UtcNow;
            IQueryable<Product> query = database.Products
                .AsNoTracking()
                .AsSplitQuery()
                .Where(product => product.Status == CatalogStatus.Published &&
                    product.ProductCategories.Any(link => ids.Contains(link.CategoryId)) &&
                    product.Variants.Any(variant => variant.Status == CatalogStatus.Published &&
                        variant.PriceChanges.Any(price => price.EffectiveAt <= now)))
                .OrderBy(product => product.Name)
                .ThenBy(product => product.Id);

            int? totalCount = null;
            if (paginated)
            {
                totalCount = await query.CountAsync(cancellationToken);
                query = query.Skip((int)offset).Take(selectedPageSize);
            }

            var products = await query
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
                .ToListAsync(cancellationToken);

            var items = products
                .Select(product => ProductDetailsBuilder.Build(product, now))
                .OfType<ProductDetailsResponse>()
                .ToArray();

            ProductListPaginationResponse? pagination = null;
            if (totalCount is int count)
            {
                var totalPages = (int)(((long)count + selectedPageSize - 1) / selectedPageSize);
                pagination = new ProductListPaginationResponse(
                    selectedPage, selectedPageSize, count, totalPages,
                    selectedPage > 1, selectedPage < totalPages);
            }

            return Ok(new ProductCategoryListResponse(items, pagination));
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
}
