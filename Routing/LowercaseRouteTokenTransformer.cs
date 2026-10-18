using Microsoft.AspNetCore.Routing;

namespace E_commerce_backend.Routing;

internal sealed class LowercaseRouteTokenTransformer : IOutboundParameterTransformer
{
    public string? TransformOutbound(object? value) =>
        value?.ToString()?.ToLowerInvariant();
}
