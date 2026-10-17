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
}
