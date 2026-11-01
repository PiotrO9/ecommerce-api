using E_commerce_backend.Data;
using E_commerce_backend.Entities;
using Microsoft.EntityFrameworkCore;

namespace E_commerce_backend.Services.Pricing;

public static class CurrentVariantPrices
{
    public static IQueryable<VariantPriceChange> Query(AppDbContext db, Guid[] variantIds, DateTime now) =>
        db.VariantPriceChanges.AsNoTracking().Where(price => variantIds.Contains(price.VariantId) &&
            price.EffectiveAt <= now && price.Id == db.VariantPriceChanges
                .Where(candidate => candidate.VariantId == price.VariantId && candidate.Currency == price.Currency && candidate.EffectiveAt <= now)
                .OrderByDescending(candidate => candidate.EffectiveAt)
                .ThenByDescending(candidate => candidate.CreatedAt)
                .ThenByDescending(candidate => candidate.Id)
                .Select(candidate => candidate.Id).First());
}
