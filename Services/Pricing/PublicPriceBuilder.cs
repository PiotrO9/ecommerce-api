using E_commerce_backend.Contracts.Catalog;
using E_commerce_backend.Entities;

namespace E_commerce_backend.Services.Pricing;

public static class PublicPriceBuilder
{
    public static ProductVariantPriceResponse Build(decimal amount, decimal basis, string currency,
        Guid variantId, PriceReduction? reduction) => new(amount, currency, basis,
        reduction is null ? null : new PublicPriceReduction(reduction.PriorAmount, reduction.ReferenceStart,
            reduction.StartedAt, reduction.Basis, reduction.PriorAmount <= 0 ? 0 :
                decimal.Round((reduction.PriorAmount - amount) / reduction.PriorAmount * 100, 2, MidpointRounding.AwayFromZero)), variantId);
}
