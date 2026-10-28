namespace E_commerce_backend.Contracts.Catalog;

public sealed record ProductFilterOptionResponse(
    string Code,
    string DisplayName,
    IReadOnlyList<string> Values);
