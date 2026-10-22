namespace E_commerce_backend.Services.Development;

public sealed class DemoSeedRequest
{
    public string? Categories { get; init; }
    public string? Products { get; init; }
    public string? Variants { get; init; }
}

public sealed record DemoSeedRange(int Min, int Max)
{
    public int Pick() => Random.Shared.Next(Min, Max + 1);
}

public sealed record DemoSeedOptions(DemoSeedRange Categories, DemoSeedRange Products, DemoSeedRange VariantsPerProduct)
{
    public static bool TryCreate(DemoSeedRequest? request, out DemoSeedOptions? options, out string? error)
    {
        options = null;
        error = null;

        if (!TryReadSize(request?.Categories, "categories", out var categories, out error) ||
            !TryReadSize(request?.Products, "products", out var products, out error) ||
            !TryReadSize(request?.Variants, "variants", out var variants, out error))
        {
            return false;
        }

        options = new DemoSeedOptions(
            categories switch { 0 => new(10, 20), 1 => new(25, 40), _ => new(50, 80) },
            products switch { 0 => new(50, 100), 1 => new(200, 300), _ => new(400, 500) },
            variants switch { 0 => new(1, 2), 1 => new(3, 4), _ => new(5, 6) });
        return true;
    }

    private static bool TryReadSize(string? value, string field, out int size, out string? error)
    {
        error = null;
        switch (value?.Trim().ToLowerInvariant())
        {
            case null or "":
                size = field == "variants" ? 0 : 1;
                return true;
            case "low":
                size = 0;
                return true;
            case "med" or "medium":
                size = 1;
                return true;
            case "high":
                size = 2;
                return true;
            default:
                size = 0;
                error = $"{field}: dozwolone wartości to low, med, medium, high.";
                return false;
        }
    }
}
