# Baza danych i Entity Framework Core

Cel: opisać własność schematów PostgreSQL, mapowanie EF Core i sposób bezpiecznego rozwijania danych aplikacji. Szczegóły obecnej tabeli są w [user-profiles.md](user-profiles.md).

## Stan potwierdzony w kodzie i bazie

- `Program.cs` odczytuje `ConnectionStrings:SupabaseDatabase` i rejestruje `AppDbContext` z dostawcą Npgsql. Lokalnie wartość jest przechowywana w .NET user-secrets, nie w `appsettings.json` ani repozytorium.
- `Entities/UserProfile.cs` określa model w C#. `Data/AppDbContext.cs` mapuje go jawnie na `app.user_profiles`, kolumny `user_id`, `first_name`, `last_name`, klucz główny `user_id` i klucz obcy do `auth.users.id` z usuwaniem kaskadowym.
- `Data/AuthUserReference.cs` służy wyłącznie do mapowania relacji. `auth.users` jest wyłączona z migracji EF, ponieważ jej właścicielem jest Supabase Auth. Nie tworzymy ani nie zmieniamy jej przez migracje aplikacji.
- Migracja `20261017172821_CreateUserProfiles` utworzyła schemat `app` i tabelę `user_profiles`. Została zastosowana w bazie 17 października 2026 r.; EF widzi ją w swojej historii. Historia tych migracji jest w `public.__EcommerceMigrationsHistory`.
- Migracja włączyła RLS dla `app.user_profiles` i tabeli historii oraz odebrała dostęp do nich rolom `anon`, `authenticated` i `service_role`. Schemat `app` nie został dodany do schematów wystawianych przez Supabase Data API.
- `GET /auth/me` odczytuje profil przez `Services/Profile/UserProfileReader.cs`; `GET /profile/me` używa `AppDbContext` bezpośrednio. Oba odczyty filtrują po ID potwierdzonym przez Supabase Auth. `PUT /profile/me` wykonuje przez EF parametryzowany SQL `INSERT ... ON CONFLICT`, aby utworzyć lub zaktualizować profil w jednym poleceniu. W tym miejscu nie ma `SaveChangesAsync()`.

## Schematy i odpowiedzialność

Tabela `app.administrators` przechowuje PK `user_id` z FK do `auth.users.id` (`ON DELETE CASCADE`) i `created_at`. Migracja `20261031110337_AddAdministrators` włącza RLS i odbiera dostęp rolom Data API oraz PUBLIC. Zastosowano ją w skonfigurowanej bazie Supabase 31.10.2026. Nie tworzy administratorów i nie zmienia schematu `auth`. Zasady ręcznego nadania dostępu opisuje [dokument dostępu administratora](administrator-access.md). Seeder katalogu nie obejmuje tej tabeli.

| Schemat | Obecne użycie | Właściciel zmian |
| --- | --- | --- |
| `auth` | `auth.users` oraz inne obiekty uwierzytelniania | Supabase Auth |
| `app` | `user_profiles` oraz tabele katalogu, magazynu i historii cen | Backend i migracje EF Core |
| `public` | `__EcommerceMigrationsHistory` | EF Core dla tej jednej tabeli; nie jest miejscem profili |

Umieszczenie tabeli w `app` porządkuje własność danych. **Sama nazwa schematu nie przyznaje ani nie odbiera dostępu.** Liczą się także uprawnienia PostgreSQL, RLS, konfiguracja Data API oraz kontrola właściciela zasobu w backendzie. Połączenie EF z uprawnioną rolą PostgreSQL może omijać RLS; dlatego backend filtruje operacje po zweryfikowanym `user_id` i nie ufa ID przesłanemu przez klienta.

## Schemat katalogu, magazynu i cen

Poniższe dziewięć tabel ma encje, mapowanie EF Core i zastosowaną w Supabase migrację `20261022055417_AddCatalogInventoryAndPriceModels`. Historia EF została sprawdzona 22 października 2026 r.; użytkownik potwierdził widoczność tabel. Reguły i dalsze prace opisują [model katalogu](catalog-model.md), [model magazynu](inventory-model.md) oraz [model cen](pricing-model.md).

```mermaid
flowchart LR
    P["app.products<br/>id PK<br/>name, slug, status<br/>created_at, updated_at, published_at"]
    V["app.product_variants<br/>id PK, product_id FK<br/>sku UNIQUE, status<br/>description, short_description, image_urls*<br/>created_at, updated_at, published_at"]
    S["app.variant_stock<br/>id PK, variant_id UNIQUE FK<br/>quantity_on_hand<br/>created_at, updated_at"]
    C["app.variant_price_changes<br/>id PK, variant_id FK<br/>base_amount, selling_amount, currency<br/>effective_at, created_at, reason"]
    K["app.attribute_keys<br/>id PK<br/>code UNIQUE, display_name"]
    A["app.attribute_pairs<br/>id PK, attribute_key_id FK<br/>value, created_at, updated_at<br/>UNIQUE attribute_key_id + value"]
    VA["app.variant_attribute_pairs<br/>variant_id PK i FK<br/>attribute_pair_id PK i FK"]
    CAT["app.categories<br/>id PK, display_name, slug<br/>parent_id FK lub NULL<br/>created_at, updated_at"]
    PC["app.product_categories<br/>product_id PK i FK<br/>category_id PK i FK"]
    P -->|"1 → co najmniej 1"| V
    P -->|"1 → 0 lub więcej"| PC
    CAT -->|"1 → 0 lub więcej przypisań"| PC
    CAT -->|"1 → 0 lub więcej podkategorii"| CAT
    V -->|"1 → 0 lub 1"| S
    V -->|"1 → 0 lub więcej"| C
    V -->|"1 → 0 lub więcej"| VA
    K -->|"1 → 0 lub więcej"| A
    A -->|"1 → 0 lub więcej"| VA
```

`image_urls*` jest zapisywane jako `text[]`. `variant_stock` może nie istnieć dla wariantu; **brak rekordu nie oznacza ilości zero i będzie blokować zakup wariantu** po wdrożeniu checkoutu. `variant_id` w tej tabeli jest unikalnym kluczem obcym, a jej własnym kluczem głównym jest `id`.

`variant_price_changes` przechowuje kolejne, niezmienne rekordy ceny wariantu. `base_amount` oznacza cenę podstawową, a `selling_amount` cenę oferowaną po promocji; bez promocji wartości są równe. Cena bieżąca pochodzi z ostatniego obowiązującego rekordu. Wariant bez ceny może być szkicem, ale nie może być opublikowany. Przy obliczaniu najniższej ceny z 30 dni przed obniżką uwzględnia się również ostatnią cenę obowiązującą na początku tego okresu; szczegóły i otwarte kwestie są w [modelu cen](pricing-model.md).

Migracja `20261028110446_AddCurrentPlnPriceIndex` dodaje częściowy indeks dla bieżącego odczytu cen PLN: `variant_id`, `effective_at DESC`, `created_at DESC`, `id DESC`, z dołączoną `selling_amount`. Lokalny pomiar na 43 467 rekordach cen wykazał, że PostgreSQL zastępuje sortowanie historii skanem indeksowym. Migrację zastosowano na odizolowanej bazie testowej oraz 28.10.2026 w skonfigurowanej bazie Supabase; ponowna lista migracji EF nie pokazuje oczekujących zmian.

Migracja `20261029140926_AddPriceChangeOperations` rozszerza historię cen o opcjonalne `operation_key`, `changed_by_user_id`, `previous_price_change_id`. Dodaje unikalność pary autor/klucz operacji, CHECK spójności metadanych oraz FK poprzedniej ceny z `RESTRICT`. Starsze dane pozostają bez metadanych. Autor jest audytowym UUID bez FK do konta. Migrację sprawdzono na izolowanym PostgreSQL 17 z istniejącymi rekordami; 31.10.2026 zastosowano ją także w skonfigurowanej bazie Supabase. Wewnętrzny serwis korzysta z krótkiej transakcji, blokady klucza operacji i wiersza wariantu; opis reguł jest w [modelu cen](pricing-model.md).

`categories.parent_id` tworzy drzewo kategorii bez stałego limitu głębokości, a `product_categories` pozwala przypisać produkt do wielu kategorii. `categories.display_name` jest wymagane i ma od 1 do 120 znaków po przycięciu białych znaków; `slug` jest wymagany i unikalny wśród kategorii mających tego samego rodzica, także wśród kategorii głównych. Zmiana `display_name` nie aktualizuje automatycznie `slug`, ale zapisany `slug` można zmienić ręcznie po walidacji. Przeniesienie kategorii wymaga wolnego `slug` w nowym miejscu i nie może utworzyć cyklu. Ścieżkę kategorii prezentujemy od korzenia do wybranego węzła. Pobranie produktów z kategorii obejmuje jej bezpośrednie przypisania oraz przypisania ze wszystkich podkategorii; wynik nie duplikuje produktów. Kategorii z podkategoriami lub przypisanymi produktami nie wolno usunąć. Szkic produktu może nie mieć kategorii, lecz publikacja wymaga przynajmniej jednego przypisania; projekt zabezpieczeń współbieżności i szczegóły są w [modelu katalogu](catalog-model.md#kategorie).

Produkt i wariant mają osobne statusy `Draft`, `Published` i `Hidden`. Status produktu jest nadrzędny: ukrycie produktu ukrywa wszystkie warianty bez zmiany ich statusów. Wariant może być widoczny tylko wtedy, gdy oba poziomy mają `Published`; szczegółowe reguły są w [modelu katalogu](catalog-model.md).

Samo FK z wariantu do produktu nie wymusi minimum jednego wariantu na produkt ani unikalnego zestawu par cech. Migracja zawiera odroczone wyzwalacze sprawdzające oba warunki na końcu transakcji, z blokadą wiersza produktu przy kontroli zestawów cech. Wymagają weryfikacji na PostgreSQL, zwłaszcza przy równoczesnych zapisach. Migracja włącza RLS i odbiera role Data API dostęp do nowych tabel. Nie zmienia schematu `auth`.

## Zasada przy dodawaniu kolejnej tabeli

Uzgodniony kierunek to tworzenie danych aplikacji w `app`, ale **EF nie ma ustawionego domyślnego schematu**. Przy nowej encji należy jawnie dodać mapowanie do `app` w `AppDbContext`, przygotować migrację i przejrzeć jej SQL przed zastosowaniem. Migracja aplikacji nie powinna modyfikować obiektów `auth` należących do Supabase. Jeśli tabela ma być osiągalna przez Supabase Data API, osobno ustal uprawnienia i polityki RLS; obecny profil jest udostępniany przez własne API.

Po zmianie modelu: wygeneruj migrację EF, sprawdź zakres tworzonych obiektów, oceń wpływ na seeder według [zasad jego utrzymania](seeder-maintenance.md), uruchom build i właściwe testy, a dopiero potem zastosuj migrację w wybranej bazie. Polecenie `dotnet ef migrations list` pokazuje historię, a `dotnet ef database update` stosuje oczekujące migracje. Szczegóły weryfikacji i otwarte punkty dla profilu są w [user-profiles.md](user-profiles.md).

## Otwarte sprawdzenia

Migracja jest zastosowana, ale nadal trzeba sprawdzić w prawdziwej bazie metadane klucza obcego i uprawnień oraz przeprowadzić test dwóch kont przez API. Testy jednostkowe nie potwierdzają tych właściwości środowiska Supabase.
