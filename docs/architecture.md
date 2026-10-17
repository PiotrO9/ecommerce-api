# Architektura backendu

Cel: dać osobie i asystentowi AI krótki punkt wejścia do obecnego kodu, jego granic i przepływu żądań. Szczegóły endpointów są w [api-contract.md](api-contract.md), a danych i migracji w [database.md](database.md).

## Stan potwierdzony w kodzie

- Repozytorium zawiera API ASP.NET Core na .NET 10. `Program.cs` rejestruje kontrolery, klienta HTTP Supabase Auth, ASP.NET Core Data Protection i `AppDbContext` z Npgsql.
- `Controllers/WebApiControllerBase.cs` ustawia prefiks tras `api/v1/web/[controller]`. `Controllers/AuthController.cs` obsługuje rejestrację, logowanie, odczyt bieżącego użytkownika i wylogowanie. `Controllers/ProfileController.cs` obsługuje profil zalogowanego użytkownika.
- `Contracts/` zawiera modele wejściowe, `Services/Auth/` komunikację z Supabase Auth i obsługę ciasteczka, `Services/Profile/` odczyt profilu, `Entities/` modele danych, a `Data/` mapowanie EF Core i migracje.
- `SupabaseAuthClient` wywołuje Supabase Auth przez HTTP. Supabase tworzy konto, sprawdza hasło, wystawia tokeny i zwraca dane uwierzytelnionego użytkownika. Backend nie przechowuje haseł ani nie wystawia własnych JWT.
- Po logowaniu `AuthCookieService` zapisuje otrzymane access token i refresh token wraz ze stałym terminem sesji w jednej wartości chronionej przez Data Protection. Przeglądarka otrzymuje ciasteczko `HttpOnly; Secure; SameSite=Lax`, bez tokenów w JSON. Bez `rememberMe` termin wynosi 1 dzień od logowania, z `rememberMe` 30 dni; odświeżenie tokenów go nie przesuwa.
- Przy chronionym żądaniu `AuthenticatedUserService` odczytuje ciasteczko i sprawdza użytkownika przez Supabase Auth. Po odrzuceniu access tokenu próbuje wymienić parę tokenów i ponawia sprawdzenie. Dopiero zweryfikowany identyfikator użytkownika jest używany do odczytu jego danych z PostgreSQL.
- `AppDbContext` mapuje `UserProfile` do `app.user_profiles`. `auth.users` należy do Supabase; EF tylko odwołuje się do jej `id` jako klucza obcego. Schematy i uprawnienia opisuje [database.md](database.md).
- `GET /api/v1/web/auth/me` łączy dane z Supabase Auth (`id`, `email`) z profilem aplikacji (`firstName`, `lastName`). Gdy profil nie istnieje, oba pola profilu są `null`.

## Granice i powody decyzji

- Klient rozmawia z naszym API; API rozmawia z Supabase Auth i bazą PostgreSQL. Ta granica pozwala trzymać poświadczenia sesji poza kodem JavaScript i ustalać dostęp do danych aplikacji na podstawie zweryfikowanego `user_id`.
- E-mail nie jest kluczem profilu, bo może się zmienić. Wspólnym identyfikatorem konta i profilu jest UUID z Supabase Auth.
- Połączenie EF do PostgreSQL nie przenosi automatycznie tożsamości użytkownika HTTP do RLS. Każdy odczyt i zapis danych konkretnego użytkownika w naszym API musi być ograniczony jego zweryfikowanym ID.

## Plany i sprawy otwarte

- Logowanie przez Google, a ewentualnie później Apple, jest planem. W kodzie działa obecnie logowanie e-mail/hasło. Szczegóły późniejszego wdrożenia opisuje [auth-implementation-plan.md](auth-implementation-plan.md).
- Repozytorium nie zawiera frontendu. Konfiguracja CORS, `SameSite` i ochrony CSRF wymaga ponownej oceny, gdy będzie znany sposób hostowania frontendu względem API.
- Integracja profilu z prawdziwymi kontami i weryfikacja dostępu między dwoma użytkownikami pozostają do sprawdzenia; testy lokalne używają atrap. Stan weryfikacji jest w [user-profiles.md](user-profiles.md) i [testing.md](testing.md).
