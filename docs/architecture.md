# Architektura backendu

Cel: dać osobie i asystentowi AI krótki punkt wejścia do obecnego kodu, jego granic i przepływu żądań. Szczegóły endpointów są w [api-contract.md](api-contract.md), a danych i migracji w [database.md](database.md).

## Stan potwierdzony w kodzie

- Repozytorium zawiera API ASP.NET Core na .NET 10. `Program.cs` rejestruje kontrolery, klienta HTTP Supabase Auth, ASP.NET Core Data Protection i `AppDbContext` z Npgsql.
- `Program.cs` generuje dokument OpenAPI z endpointów oraz udostępnia Swagger UI w środowisku `Development` pod `/swagger`; JSON jest pod `/openapi/v1.json`.
- `Controllers/WebApiControllerBase.cs` ustawia prefiks tras `api/v1/web/[controller]` dla kontrolerów, które po nim dziedziczą. Konwencja w `Program.cs` zamienia token nazwy kontrolera na małe litery, także w generowanym OpenAPI. `Controllers/AuthController.cs` obsługuje rejestrację, logowanie, odczyt bieżącego użytkownika i wylogowanie. `Controllers/ProfileController.cs` obsługuje profil zalogowanego użytkownika. `Controllers/CategoriesController.cs` zwraca całe drzewo kategorii, gałąź wskazaną przez `rootId` albo dane jednej kategorii po UUID. `Controllers/ProductsController.cs` zwraca szczegóły opublikowanego produktu, proste listy GET oraz zaawansowane wyszukiwanie `POST /products/search` z filtrami w body JSON. `PasswordResetController` deklaruje trasę `api/v1/web/auth/password-reset` bezpośrednio.
- `Contracts/` zawiera modele wejściowe, `Services/Auth/` komunikację z Supabase Auth i obsługę ciasteczka, `Services/Profile/` odczyt profilu, `Entities/` modele danych, a `Data/` mapowanie EF Core i migracje.
- Listy produktów projektują tylko pola kafelka, pobierają warianty i ich ostatnie obowiązujące ceny osobnymi zapytaniami; szczegóły nadal buduje `ProductDetailsBuilder`. `GET /products/filter-options` wyznacza dostępne pary cech z widocznych wariantów, a odczyt pojedynczej kategorii dodaje ścieżkę od korzenia.
- `POST /products/search` używa wspólnej konstrukcji zapytania po widocznych, wycenionych wariantach do filtrowania produktów i wyliczania kontekstowych opcji cech. Liczniki agreguje baza po różnych produktach; dla wybranego klucza pomijany jest jego własny filtr.
- `SupabaseAuthClient` wywołuje Supabase Auth przez HTTP. Supabase tworzy konto, sprawdza hasło, wystawia tokeny i zwraca dane uwierzytelnionego użytkownika. Backend nie przechowuje haseł ani nie wystawia własnych JWT.
- `PasswordResetController` używa osobnego klienta z tajnym kluczem Supabase tylko do wygenerowania linku odzyskiwania. `MailtrapEmailSender` wysyła wiadomość przez Mailtrap Transactional API z adresem i nazwą nadawcy oraz kategorią wiadomości; nie wymaga SDK. Weryfikacja tokenu i zmiana hasła przechodzą przez klienta Supabase z kluczem publikowalnym i tokenem użytkownika. Backend nie przechowuje tokenów resetu.
- Po logowaniu `AuthCookieService` zapisuje otrzymane access token i refresh token wraz ze stałym terminem sesji w jednej wartości chronionej przez Data Protection. Przeglądarka otrzymuje ciasteczko `HttpOnly; Secure; SameSite=Lax`, bez tokenów w JSON. Bez `rememberMe` termin wynosi 1 dzień od logowania, z `rememberMe` 30 dni; odświeżenie tokenów go nie przesuwa.
- Przy chronionym żądaniu `AuthenticatedUserService` odczytuje ciasteczko i sprawdza użytkownika przez Supabase Auth. Po odrzuceniu access tokenu próbuje wymienić parę tokenów i ponawia sprawdzenie. Dopiero zweryfikowany identyfikator użytkownika jest używany do odczytu jego danych z PostgreSQL.
- `AppDbContext` mapuje `UserProfile` do `app.user_profiles`. `auth.users` należy do Supabase; EF tylko odwołuje się do jej `id` jako klucza obcego. Schematy i uprawnienia opisuje [database.md](database.md).
- `AppDbContext` mapuje też encje katalogu, kategorii, stanu wariantu i historii jego cen do schematu `app`. Migracja dla tych tabel jest zastosowana w Supabase; API ma odczyt kategorii, szczegółów i list produktów, lecz nie ma jeszcze administracyjnego zapisu katalogu.
- `GET /api/v1/web/auth/me` łączy dane z Supabase Auth (`id`, `email`) z profilem aplikacji (`firstName`, `lastName`). Gdy profil nie istnieje, oba pola profilu są `null`.

## Generowanie dokumentacji API

`AddControllers()` i `MapControllers()` rejestrują endpointy z kontrolerów oraz atrybutów takich jak `[HttpGet]`, `[HttpPost]` i `[HttpPut]`. Konwencja `RouteTokenTransformerConvention` z `Routing/LowercaseRouteTokenTransformer.cs` zamienia wartość tokenu `[controller]` na małe litery. `AddOpenApi()` korzysta z metadanych zarejestrowanych endpointów, a `MapOpenApi()` wystawia wynik pod `/openapi/v1.json`. Swagger UI odczytuje ten dokument; oba adresy są dostępne tylko w środowisku `Development`.

Po zmianie kodu endpointu trzeba przebudować i ponownie uruchomić aplikację, aby nowa wersja pojawiła się w Swagger UI. Nazwy tras i modele żądań są odkrywane automatycznie; pełne opisy odpowiedzi wymagają metadanych dodanych przy endpointach. Nagłówek `X-Requested-With` jest zadeklarowany przez `[FromHeader]` w `PUT /profile/me` i `POST /auth/logout`, dzięki czemu Swagger UI pokazuje pole do wpisania jego wartości.

## Granice i powody decyzji

- Klient rozmawia z naszym API; API rozmawia z Supabase Auth i bazą PostgreSQL. Ta granica pozwala trzymać poświadczenia sesji poza kodem JavaScript i ustalać dostęp do danych aplikacji na podstawie zweryfikowanego `user_id`.
- E-mail nie jest kluczem profilu, bo może się zmienić. Wspólnym identyfikatorem konta i profilu jest UUID z Supabase Auth.
- Połączenie EF do PostgreSQL nie przenosi automatycznie tożsamości użytkownika HTTP do RLS. Każdy odczyt i zapis danych konkretnego użytkownika w naszym API musi być ograniczony jego zweryfikowanym ID.

## Plany i sprawy otwarte

- Logowanie przez Google, a ewentualnie później Apple, jest planem. W kodzie działa obecnie logowanie e-mail/hasło. Szczegóły późniejszego wdrożenia opisuje [auth-implementation-plan.md](auth-implementation-plan.md).
- Repozytorium nie zawiera frontendu. Konfiguracja CORS, `SameSite` i ochrony CSRF wymaga ponownej oceny, gdy będzie znany sposób hostowania frontendu względem API.
- Integracja profilu z prawdziwymi kontami i weryfikacja dostępu między dwoma użytkownikami pozostają do sprawdzenia; testy lokalne używają atrap. Stan weryfikacji jest w [user-profiles.md](user-profiles.md) i [testing.md](testing.md).
