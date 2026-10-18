using System.Text.Json.Serialization;

namespace E_commerce_backend.Services.Auth;

internal sealed class RecoveryLinkResponse
{
    [JsonPropertyName("hashed_token")]
    public string? HashedToken { get; init; }
}
