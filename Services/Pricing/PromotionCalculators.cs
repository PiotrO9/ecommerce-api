using E_commerce_backend.Entities;

namespace E_commerce_backend.Services.Pricing;

public static class PromotionCalculator
{
    public static decimal? Calculate(decimal basis, string type, decimal value)
    {
        if (basis < 0 || value <= 0 || decimal.Round(value, 2) != value) return null;
        var result = type switch
        {
            "percentage" when value <= 100 => basis * (1 - value / 100),
            "amount" when value <= basis => basis - value,
            _ => -1
        };
        return result < 0 ? null : decimal.Round(result, 2, MidpointRounding.AwayFromZero);
    }
}

public sealed record PriorPrice(decimal Amount, DateTime ReferenceStart, string Basis);

public static class PriorPriceCalculator
{
    private static readonly TimeZoneInfo Warsaw = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");

    public static DateTime WindowStart(DateTime now)
    {
        var local = DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(now, Warsaw).AddDays(-30), DateTimeKind.Unspecified);
        while (Warsaw.IsInvalidTime(local)) local = local.AddMinutes(-1);
        if (Warsaw.IsAmbiguousTime(local))
            return new DateTimeOffset(local, Warsaw.GetAmbiguousTimeOffsets(local).Max()).UtcDateTime;
        return TimeZoneInfo.ConvertTimeToUtc(local, Warsaw);
    }

    public static PriorPrice? Calculate(IEnumerable<VariantPriceChange> history,
        IEnumerable<VariantOfferEvent> offerHistory, DateTime now)
    {
        var prices = history.Where(p => p.Currency == "PLN" && p.EffectiveAt < now)
            .OrderBy(p => p.EffectiveAt).ThenBy(p => p.CreatedAt).ThenBy(p => p.Id).ToArray();
        var events = offerHistory.Where(e => e.OccurredAt < now)
            .OrderBy(e => e.OccurredAt).ThenBy(e => e.Id).ToArray();
        if (events.Length == 0 || prices.Length == 0) return null;
        var window = WindowStart(now);
        if (!events[0].CompleteFromBirth && events[0].OccurredAt > window) return null;
        var points = prices.Select(p => p.EffectiveAt).Concat(events.Select(e => e.OccurredAt))
            .Append(now).Distinct().Order().ToArray();
        var pi = 0; var ei = 0;
        VariantPriceChange? price = null;
        VariantOfferEvent? state = null;
        DateTime? firstOffer = null;
        decimal? min = null;
        for (var i = 0; i < points.Length - 1; i++)
        {
            var at = points[i];
            while (pi < prices.Length && prices[pi].EffectiveAt <= at) price = prices[pi++];
            while (ei < events.Length && events[ei].OccurredAt <= at) state = events[ei++];
            if (state?.Visible != true || price is null || points[i + 1] <= at) continue;
            firstOffer ??= at;
            if (points[i + 1] > window) min = min is null ? price.SellingAmount : Math.Min(min.Value, price.SellingAmount);
        }
        if (firstOffer is null || min is null) return null;
        var start = firstOffer > window && events[0].CompleteFromBirth ? firstOffer.Value : window;
        return new(min.Value, start, start > window ? "SinceFirstOffer" : "Full30Days");
    }
}
