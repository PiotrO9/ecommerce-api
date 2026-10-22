using E_commerce_backend.Contracts.Catalog;
using E_commerce_backend.Data;
using E_commerce_backend.Services.Catalog;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace E_commerce_backend.Controllers;

public sealed class CategoriesController(AppDbContext database) : WebApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetTree([FromQuery] Guid? rootId, CancellationToken cancellationToken)
    {
        try
        {
            var categories = await database.Categories.AsNoTracking().ToListAsync(cancellationToken);
            var trees = CategoryTreeBuilder.Build(categories, rootId);
            return trees is null
                ? NotFound(new { message = "Nie znaleziono kategorii." })
                : Ok(trees);
        }
        catch (NpgsqlException)
        {
            return StatusCode(503, new { message = "Kategorie są chwilowo niedostępne." });
        }
        catch (TimeoutException)
        {
            return StatusCode(503, new { message = "Kategorie są chwilowo niedostępne." });
        }
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var category = await database.Categories.AsNoTracking()
                .Where(category => category.Id == id)
                .Select(category => new CategoryDetailsResponse(
                    category.Id,
                    category.DisplayName,
                    category.Slug,
                    category.ParentId,
                    category.CreatedAt,
                    category.UpdatedAt))
                .SingleOrDefaultAsync(cancellationToken);

            return category is null
                ? NotFound(new { message = "Nie znaleziono kategorii." })
                : Ok(category);
        }
        catch (NpgsqlException)
        {
            return StatusCode(503, new { message = "Kategorie są chwilowo niedostępne." });
        }
        catch (TimeoutException)
        {
            return StatusCode(503, new { message = "Kategorie są chwilowo niedostępne." });
        }
    }
}
