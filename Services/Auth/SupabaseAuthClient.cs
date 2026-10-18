using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace E_commerce_backend.Services.Auth;

public sealed class SupabaseAuthClient
{
    private readonly HttpClient _httpClient;

    public SupabaseAuthClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public Task<HttpResponseMessage> SignUpAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        return _httpClient.PostAsJsonAsync(
            "auth/v1/signup",
            new { email, password },
            cancellationToken);
    }

    public Task<HttpResponseMessage> SignInAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        return _httpClient.PostAsJsonAsync(
            "auth/v1/token?grant_type=password",
            new { email, password },
            cancellationToken);
    }

    public Task<HttpResponseMessage> RefreshSessionAsync(
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        return _httpClient.PostAsJsonAsync(
            "auth/v1/token?grant_type=refresh_token",
            new { refresh_token = refreshToken },
            cancellationToken);
    }

    public async Task<HttpResponseMessage> GetUserAsync(
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "auth/v1/user");
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        return await _httpClient.SendAsync(request, cancellationToken);
    }

    public async Task<HttpResponseMessage> SignOutAsync(
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "auth/v1/logout?scope=local");
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        return await _httpClient.SendAsync(request, cancellationToken);
    }

    public Task<HttpResponseMessage> VerifyRecoveryAsync(string tokenHash, CancellationToken cancellationToken) =>
        _httpClient.PostAsJsonAsync("auth/v1/verify", new { token_hash = tokenHash, type = "recovery" }, cancellationToken);

    public async Task<HttpResponseMessage> UpdatePasswordAsync(string accessToken, string password, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, "auth/v1/user")
        {
            Content = JsonContent.Create(new { password })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await _httpClient.SendAsync(request, cancellationToken);
    }
}
