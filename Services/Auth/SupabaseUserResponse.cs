using System.Text.Json.Serialization;

namespace E_commerce_backend.Services.Auth;

internal sealed class SupabaseUserResponse
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("email")]
    public string? Email { get; init; }
}
