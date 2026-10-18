using System.Text.Json.Serialization;

namespace E_commerce_backend.Services.Auth;

internal sealed class RecoverySessionResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; init; }
}
