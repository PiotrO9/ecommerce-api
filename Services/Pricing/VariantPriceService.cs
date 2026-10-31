using E_commerce_backend.Data;
using E_commerce_backend.Entities;
using Microsoft.EntityFrameworkCore;

namespace E_commerce_backend.Services.Pricing;

public sealed class VariantPriceService(AppDbContext database, TimeProvider timeProvider)
{
    public async Task<PriceChangeResult> ChangeAsync(ChangeVariantPrice command,
        CancellationToken cancellationToken = default)
    {
        var currency = command.Currency?.Trim().ToUpperInvariant() ?? "PLN";
        var reason = command.Reason?.Trim();
        if (command.VariantId == Guid.Empty || command.ActorUserId == Guid.Empty ||
            command.OperationKey == Guid.Empty || command.ExpectedPriceChangeId == Guid.Empty ||
            currency != "PLN" || string.IsNullOrEmpty(reason) || reason.Length > 120 ||
            command.BaseAmount < 0 || command.BaseAmount > 9999999999999999.99m ||
            decimal.Round(command.BaseAmount, 2) != command.BaseAmount)
        {
            return new(PriceChangeStatus.InvalidRequest);
        }

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        // Serialize retries even when the same key is accidentally used for different variants.
        var lockKey = $"variant-price:{command.ActorUserId:D}:{command.OperationKey:D}";
        await database.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))", cancellationToken);

        var previousOperation = await database.VariantPriceChanges.AsNoTracking().SingleOrDefaultAsync(
            price => price.ChangedByUserId == command.ActorUserId && price.OperationKey == command.OperationKey,
            cancellationToken);
        if (previousOperation is not null)
        {
            var sameRequest = previousOperation.VariantId == command.VariantId &&
                previousOperation.BaseAmount == command.BaseAmount && previousOperation.Currency == currency &&
                previousOperation.Reason == reason &&
                previousOperation.PreviousPriceChangeId == command.ExpectedPriceChangeId;
            return sameRequest
                ? new(PriceChangeStatus.Replayed, Receipt(previousOperation))
                : new(PriceChangeStatus.OperationKeyConflict);
        }

        // The variant row exists before its first price, so it also serializes first writes.
        var variant = await database.ProductVariants.FromSqlInterpolated(
                $"SELECT * FROM app.product_variants WHERE id = {command.VariantId} FOR UPDATE")
            .AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (variant is null)
        {
            return new(PriceChangeStatus.VariantNotFound);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        // PostgreSQL timestamps have microsecond precision; avoid introducing an ambiguous tie.
        now = new DateTime(now.Ticks - now.Ticks % 10, DateTimeKind.Utc);
        var prices = database.VariantPriceChanges.AsNoTracking()
            .Where(price => price.VariantId == command.VariantId && price.Currency == currency);
        if (await prices.AnyAsync(price => price.EffectiveAt > now, cancellationToken))
        {
            return new(PriceChangeStatus.FuturePriceConflict);
        }

        var current = await prices.OrderByDescending(price => price.EffectiveAt)
            .ThenByDescending(price => price.CreatedAt).ThenByDescending(price => price.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (current?.Id != command.ExpectedPriceChangeId)
        {
            return new(PriceChangeStatus.VersionConflict);
        }
        if (current is not null && current.BaseAmount != current.SellingAmount)
        {
            return new(PriceChangeStatus.DiscountedPriceConflict);
        }
        if (current is not null && current.EffectiveAt >= now)
        {
            return new(PriceChangeStatus.TimestampConflict);
        }

        var change = new VariantPriceChange
        {
            VariantId = command.VariantId,
            BaseAmount = command.BaseAmount,
            SellingAmount = command.BaseAmount,
            Currency = currency,
            Reason = reason,
            EffectiveAt = now,
            CreatedAt = now,
            OperationKey = command.OperationKey,
            ChangedByUserId = command.ActorUserId,
            PreviousPriceChangeId = current?.Id
        };
        database.VariantPriceChanges.Add(change);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            // A failed write must not be retried by a later SaveChanges on the same context.
            database.Entry(change).State = EntityState.Detached;
        }
        return new(PriceChangeStatus.Applied, Receipt(change));
    }

    private static PriceChangeReceipt Receipt(VariantPriceChange price) => new(price.Id,
        price.VariantId, price.BaseAmount, price.SellingAmount, price.Currency,
        price.EffectiveAt, price.CreatedAt, price.Reason, price.PreviousPriceChangeId);
}
