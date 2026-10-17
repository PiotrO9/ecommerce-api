using System.Text.Json.Serialization;

namespace E_commerce_backend.Services.Auth;

internal sealed class SupabaseSignInResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; init; }

    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; init; }
}
