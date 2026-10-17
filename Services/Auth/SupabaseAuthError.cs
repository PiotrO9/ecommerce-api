using System.Text.Json.Serialization;

namespace E_commerce_backend.Services.Auth;

internal sealed class SupabaseAuthError
{
    [JsonPropertyName("error_code")]
    public string? ErrorCode { get; set; }
}
