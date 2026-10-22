namespace E_commerce_backend.Entities;

public sealed class AttributeKey
{
    private string _code = null!;

    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Code
    {
        get => _code;
        set => _code = NormalizeCode(value);
    }
    public required string DisplayName { get; set; }
    public ICollection<AttributePair> Pairs { get; set; } = new List<AttributePair>();

    public static string NormalizeCode(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        var normalized = code.Trim().ToLowerInvariant();
        return normalized.Length == 0
            ? throw new ArgumentException("Attribute code cannot be empty.", nameof(code))
            : normalized;
    }
}
