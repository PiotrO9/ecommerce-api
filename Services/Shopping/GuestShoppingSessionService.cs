using E_commerce_backend.Contracts.Shopping;
using E_commerce_backend.Data;
using E_commerce_backend.Entities;
using Microsoft.EntityFrameworkCore;

namespace E_commerce_backend.Services.Shopping;

public sealed class GuestShoppingSessionService(AppDbContext db, ShoppingStore store, TimeProvider time)
{
    public async Task<ShoppingResult> CreateAsync(ShoppingOwner owner, HttpResponse response, CancellationToken ct)
    {
        if (owner.UserId is not null) return ShoppingResult.Error(409, "account_session_active", "Koszyk gościa jest dostępny po wylogowaniu.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await ShoppingWriteLock.SharedAsync(db, ct);
        var previous = owner.GuestTokenHash is null ? null : await store.CartAsync(owner, true, ct);
        if (previous is not null && !store.IsExpired(previous))
        {
            await tx.CommitAsync(ct);
            return new(200, new GuestShoppingSessionResponse(previous.Id, previous.CartVersion, previous.ExpiresAt!.Value));
        }
        var now = time.GetUtcNow().UtcDateTime;
        var token = GuestShoppingCookie.Create();
        var state = new ShoppingState { GuestTokenHash = token.Hash, ExpiresAt = now.AddDays(30), CreatedAt = now, UpdatedAt = now };
        db.ShoppingStates.Add(state);
        await db.SaveChangesAsync(ct);
        // Return the persisted microsecond precision used by PostgreSQL on retries.
        await db.Entry(state).ReloadAsync(ct);
        await tx.CommitAsync(ct);
        GuestShoppingCookie.Append(response, token.Token, state.ExpiresAt.Value);
        return new(201, new GuestShoppingSessionResponse(state.Id, 0, state.ExpiresAt.Value));
    }

    public async Task<ShoppingResult> DeleteAsync(string? hash, HttpResponse response, CancellationToken ct)
    {
        if (hash is not null)
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await ShoppingWriteLock.SharedAsync(db, ct);
            await db.ShoppingStates.Where(s => s.UserId == null && s.GuestTokenHash == hash).ExecuteDeleteAsync(ct);
            await tx.CommitAsync(ct);
        }
        GuestShoppingCookie.Delete(response);
        return new(204);
    }
}
