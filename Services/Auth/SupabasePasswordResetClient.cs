using System.Net.Http.Json;

namespace E_commerce_backend.Services.Auth;

public sealed class SupabasePasswordResetClient(HttpClient httpClient)
{
    public Task<HttpResponseMessage> GenerateLinkAsync(string email, CancellationToken cancellationToken) =>
        httpClient.PostAsJsonAsync("auth/v1/admin/generate_link", new { type = "recovery", email }, cancellationToken);

}
