# Profile użytkowników

Cel: opisać powiązanie kont Supabase Auth z danymi profilu aplikacji przechowywanymi przez EF Core.

## Stan w kodzie

- `AppDbContext` mapuje `UserProfile` do `app.user_profiles`. `user_id` jest kluczem głównym profilu i kluczem obcym do istniejącego `auth.users.id`, z `ON DELETE CASCADE`. Encja `AuthUserReference` służy jedynie do mapowania relacji i jest wyłączona z migracji EF; aplikacja nie zarządza tabelą Supabase Auth.
- Pierwsze pola profilu to wymagane `first_name` i `last_name`, każde o maksymalnej długości 100 znaków. Profil powstaje przy pierwszym zapisie, nie podczas rejestracji w Supabase.
- Migracja `CreateUserProfiles` tworzy schemat i tabelę aplikacji, włącza RLS i odbiera uprawnienia do tabeli rolom Data API. Migracja nie dodaje schematu `app` do listy schematów wystawianych przez Supabase Data API. Przez połączenie PostgreSQL backend może działać z uprawnieniami, które omijają RLS, więc własne API musi zawsze ograniczać operację do zweryfikowanego `user_id`.
- EF zapisuje historię migracji w osobnej tabeli `public.__EcommerceMigrationsHistory`; migracja zabezpiecza ją przez RLS i odebranie uprawnień rolom Data API, nie zmieniając historii migracji innych aplikacji.
- `GET /api/v1/web/profile/me` zwraca profil albo `404`, jeśli nie został jeszcze zapisany. `PUT /api/v1/web/profile/me` przyjmuje tylko imię i nazwisko, tworzy lub aktualizuje profil atomowym `INSERT ... ON CONFLICT` wykonywanym przez EF Core. Oba endpointy ustalają identyfikator właściciela przez Supabase Auth i obsługują odświeżanie sesji. `PUT` wymaga nagłówka `X-Requested-With: XMLHttpRequest`.
- `GET /api/v1/web/auth/me` zwraca `id` i `email` z Supabase Auth oraz `firstName` i `lastName` z profilu aplikacji. Jeśli profil nie został jeszcze zapisany, obie nazwy mają wartość `null`; niedostępność bazy daje `503`. Odczyt profilu następuje dopiero po uwierzytelnieniu i filtruje po zweryfikowanym `user_id`.
- `Program.cs` wymaga `ConnectionStrings:SupabaseDatabase`. Lokalnie należy zapisać wartość w .NET user-secrets. To connection string PostgreSQL dla Npgsql, odrębny od adresu i klucza publikowalnego Supabase Auth. W kodzie i dokumentacji nie zapisujemy hasła bazy.

## Stan w bazie

Migracja `20261017172821_CreateUserProfiles` została zastosowana 17 października 2026 r. w projekcie Supabase przez `dotnet ef database update`. Po wykonaniu `dotnet ef migrations list` EF widzi ją jako zastosowaną. Polecenie migracji wykonało włączenie RLS i odebranie uprawnień rolom Data API. Nadal trzeba sprawdzić metadane klucza obcego i uprawnienia w bazie oraz przetestować odczyt i zapis profilu z prawdziwym użytkownikiem, w tym brak dostępu konta A do profilu konta B przez API.

## Dlaczego ten podział

Supabase Auth pozostaje właścicielem kont i haseł. Profil zawiera dane aplikacyjne i jest połączony stabilnym UUID, więc zmiana e-maila lub późniejsze dodanie logowania Google nie wymaga przepisywania profilu. Schemat `app` jest prywatny dla backendu; klient korzysta z własnego API. Źródła: [Supabase User Management](https://supabase.com/docs/guides/auth/managing-user-data), [Supabase Using Custom Schemas](https://supabase.com/docs/guides/api/using-custom-schemas), [Npgsql EF Core](https://www.npgsql.org/efcore/).
