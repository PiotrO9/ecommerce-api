namespace E_commerce_backend.Data;

// Tylko do zdefiniowania klucza obcego. Tabelą auth.users zarządza Supabase.
internal sealed class AuthUserReference
{
    public Guid Id { get; set; }
}
