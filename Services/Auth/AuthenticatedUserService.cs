using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace E_commerce_backend.Services.Auth;

public sealed class AuthenticatedUserService(
    SupabaseAuthClient supabaseAuthClient,
    AuthCookieService authCookieService)
{
    public async Task<AuthenticatedUserResult> GetAsync(
        HttpRequest request,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        var session = authCookieService.Read(request);
        if (session is null)
        {
            return new AuthenticatedUserResult(AuthenticatedUserStatus.Unauthenticated);
        }

        try
        {
            using var userResponse = await supabaseAuthClient.GetUserAsync(
                session.AccessToken, cancellationToken);

            if (userResponse.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                var refresh = await RefreshAsync(session, response, cancellationToken);
                if (refresh.Status == SessionRefreshStatus.Invalid)
                {
                    return new AuthenticatedUserResult(AuthenticatedUserStatus.Unauthenticated);
                }

                if (refresh.Status == SessionRefreshStatus.Unavailable)
                {
                    return new AuthenticatedUserResult(AuthenticatedUserStatus.Unavailable);
                }

                using var retry = await supabaseAuthClient.GetUserAsync(
                    refresh.Session!.AccessToken, cancellationToken);
                return await ParseUserAsync(retry, response, cancellationToken);
            }

            return await ParseUserAsync(userResponse, response, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return new AuthenticatedUserResult(AuthenticatedUserStatus.Unavailable);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new AuthenticatedUserResult(AuthenticatedUserStatus.Unavailable);
        }
    }

    public async Task<SessionRefreshResult> RefreshAsync(
        AuthSessionPayload previous,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        using var refreshResponse = await supabaseAuthClient.RefreshSessionAsync(
            previous.RefreshToken, cancellationToken);

        if (!refreshResponse.IsSuccessStatusCode)
        {
            if (refreshResponse.StatusCode == HttpStatusCode.TooManyRequests ||
                (int)refreshResponse.StatusCode >= 500)
            {
                return new SessionRefreshResult(SessionRefreshStatus.Unavailable);
            }

            string? errorCode = null;
            try
            {
                var error = await refreshResponse.Content.ReadFromJsonAsync<SupabaseAuthError>(
                    cancellationToken);
                errorCode = error?.ErrorCode;
            }
            catch (JsonException)
            {
                // Nie usuwamy sesji na podstawie nieznanej odpowiedzi Supabase.
            }

            if (errorCode is "refresh_token_not_found" or "refresh_token_already_used" or
                "session_not_found" or "session_expired")
            {
                authCookieService.Delete(response);
                return new SessionRefreshResult(SessionRefreshStatus.Invalid);
            }

            return new SessionRefreshResult(SessionRefreshStatus.Unavailable);
        }

        SupabaseSignInResponse? tokens;
        try
        {
            tokens = await refreshResponse.Content.ReadFromJsonAsync<SupabaseSignInResponse>(
                cancellationToken);
        }
        catch (JsonException)
        {
            return new SessionRefreshResult(SessionRefreshStatus.Unavailable);
        }

        if (string.IsNullOrWhiteSpace(tokens?.AccessToken) ||
            string.IsNullOrWhiteSpace(tokens.RefreshToken))
        {
            return new SessionRefreshResult(SessionRefreshStatus.Unavailable);
        }

        if (previous.AbsoluteExpiresAt <= DateTimeOffset.UtcNow)
        {
            authCookieService.Delete(response);
            return new SessionRefreshResult(SessionRefreshStatus.Invalid);
        }

        var updated = new AuthSessionPayload(
            tokens.AccessToken, tokens.RefreshToken, previous.AbsoluteExpiresAt);
        if (!authCookieService.TryAppend(response, updated))
        {
            authCookieService.Delete(response);
            return new SessionRefreshResult(SessionRefreshStatus.Unavailable);
        }

        return new SessionRefreshResult(SessionRefreshStatus.Success, updated);
    }

    private async Task<AuthenticatedUserResult> ParseUserAsync(
        HttpResponseMessage userResponse,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        if (userResponse.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            authCookieService.Delete(response);
            return new AuthenticatedUserResult(AuthenticatedUserStatus.Unauthenticated);
        }

        if (!userResponse.IsSuccessStatusCode)
        {
            return new AuthenticatedUserResult(AuthenticatedUserStatus.Unavailable);
        }

        SupabaseUserResponse? user;
        try
        {
            user = await userResponse.Content.ReadFromJsonAsync<SupabaseUserResponse>(
                cancellationToken);
        }
        catch (JsonException)
        {
            return new AuthenticatedUserResult(AuthenticatedUserStatus.Unavailable);
        }

        return user is not null && user.Id != Guid.Empty
            ? new AuthenticatedUserResult(AuthenticatedUserStatus.Success, user.Id, user.Email)
            : new AuthenticatedUserResult(AuthenticatedUserStatus.Unavailable);
    }
}
