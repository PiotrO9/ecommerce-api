using E_commerce_backend.Contracts.Profile;
using E_commerce_backend.Data;
using E_commerce_backend.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace E_commerce_backend.Controllers;

public sealed class ProfileController(
    AuthenticatedUserService authenticatedUserService,
    AppDbContext database) : WebApiControllerBase
{
    [HttpGet("me")]
    public async Task<IActionResult> GetMine(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        var user = await authenticatedUserService.GetAsync(Request, Response, cancellationToken);
        if (user.Status == AuthenticatedUserStatus.Unauthenticated)
        {
            return Unauthorized(new { message = "Brak ważnej sesji." });
        }

        if (user.Status == AuthenticatedUserStatus.Unavailable)
        {
            return StatusCode(503, new { message = "Profil jest chwilowo niedostępny." });
        }

        try
        {
            var profile = await database.UserProfiles.AsNoTracking()
                .SingleOrDefaultAsync(x => x.UserId == user.UserId!.Value, cancellationToken);

            return profile is null
                ? NotFound(new { message = "Profil nie został jeszcze uzupełniony." })
                : Ok(new
                {
                    userId = profile.UserId,
                    firstName = profile.FirstName,
                    lastName = profile.LastName
                });
        }
        catch (NpgsqlException)
        {
            return StatusCode(503, new { message = "Profil jest chwilowo niedostępny." });
        }
        catch (TimeoutException)
        {
            return StatusCode(503, new { message = "Profil jest chwilowo niedostępny." });
        }
    }

    [HttpPut("me")]
    public async Task<IActionResult> UpdateMine(
        [FromBody] UpdateMyProfileRequest request,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        if (Request.Headers["X-Requested-With"] != "XMLHttpRequest")
        {
            return StatusCode(403, new { message = "Żądanie zostało odrzucone." });
        }

        var firstName = request.FirstName.Trim();
        var lastName = request.LastName.Trim();
        if (firstName.Length == 0 || lastName.Length == 0)
        {
            return BadRequest(new { message = "Podaj imię i nazwisko." });
        }

        var user = await authenticatedUserService.GetAsync(Request, Response, cancellationToken);
        if (user.Status == AuthenticatedUserStatus.Unauthenticated)
        {
            return Unauthorized(new { message = "Brak ważnej sesji." });
        }

        if (user.Status == AuthenticatedUserStatus.Unavailable)
        {
            return StatusCode(503, new { message = "Profil jest chwilowo niedostępny." });
        }

        try
        {
            // ON CONFLICT pozwala bezpiecznie obsłużyć równoczesny pierwszy zapis profilu.
            await database.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO app.user_profiles (user_id, first_name, last_name)
                VALUES ({user.UserId!.Value}, {firstName}, {lastName})
                ON CONFLICT (user_id) DO UPDATE
                SET first_name = EXCLUDED.first_name,
                    last_name = EXCLUDED.last_name
                """, cancellationToken);

            return Ok(new
            {
                userId = user.UserId,
                firstName,
                lastName
            });
        }
        catch (NpgsqlException)
        {
            return StatusCode(503, new { message = "Profil jest chwilowo niedostępny." });
        }
        catch (TimeoutException)
        {
            return StatusCode(503, new { message = "Profil jest chwilowo niedostępny." });
        }
    }
}
