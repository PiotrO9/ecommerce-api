# Architektura backendu

Cel: dać osobie i asystentowi AI krótki punkt wejścia do obecnego kodu, jego granic i przepływu żądań. Szczegóły endpointów są w [api-contract.md](api-contract.md), a danych i migracji w [database.md](database.md).

## Koszyk i ulubione — 01.11.2026

Backend ma osobne koszyki gościa i konta oraz ulubione wariantów wyłącznie na koncie. Logowanie nie przenosi pozycji, logout zachowuje ciasteczko gościa. `ShoppingOwnerResolver` używa istniejącego `AuthenticatedUserService`; obecność nieważnego ciasteczka Auth daje `401`, niedostępność usługi `503`, bez przełączenia zapisu na gościa. `GuestShoppingCookie` przechowuje losowy token 256-bitowy w `__Host-ecomm-shopping`; baza przechowuje tylko SHA-256. Termin 30 dni jest sprawdzany po stronie backendu i nie jest przedłużany.

`ShoppingStore` rozwiązuje prywatny stan koszyka/listy. `CartService` i `FavoritesService` przy zapisie porównują `expectedScopeId` i `expectedVersion`, blokują wiersz właściciela i stosują limity 100 wariantów/99 sztuk/500 ulubionych. Triggery pozycji zwiększają wersję również po SQL i kaskadowym usunięciu wariantu. Ulubione mają własną tabelę list konta; nie wymagają uzupełnionego profilu.

`ShoppingOfferReader` ładuje wybrane warianty i ich bieżące ceny zbiorczo. `CurrentVariantPrices.Query` jest wspólne z listami katalogu, a `PublicPriceBuilder` zachowuje te same dane promocji i wcześniejszej ceny. GET-y używają `RepeatableRead` i jednego czasu oceny; nie zamrażają ceny i nie rezerwują magazynu. Ukryta oferta ma minimalny placeholder bez opisu/zdjęć. Suma koszyka obejmuje tylko dostępne pozycje i nie zawiera dostawy.

Nowe kontrolery mają ochronę zapisów przez `X-Requested-With`, JSON z odrzucaniem nieznanych pól i limity żądań. Middleware ustawia `no-store` także dla błędów walidacji/routingu. Blokada współdzielona `(20261101,4)` pozwala pracować wielu właścicielom jednocześnie; reset seedera pobiera jej postać wyłączną po blokadzie katalogu, podnosi wersje i czyści pozycje. Migracja czterech tabel została zastosowana wyłącznie lokalnie. Odbiór HTTPS z rzeczywistym Supabase Auth przeszedł 54/54 sprawdzenia na dwóch tymczasowych kontach i niezależnych sesjach; konta usunięto i zweryfikowano ich brak. Katalog pozostawał lokalny. Odbiór frontendu, domen oraz migracja i odbiór zdalnej bazy Supabase pozostają do wykonania; [szczegóły](cart-favorites-implementation-plan.md).

## Stan potwierdzony w kodzie

Magazyn (01.11.2026): `InventoryOrderService` atomowo zapisuje zamówienie z koszyka konta, rezerwację i opróżnienie koszyka. `OrdersController` zapewnia prywatną historię i anulowanie, bez endpointu rozliczenia płatności. `InventoryAvailability` zbiorczo liczy niewygasłe rezerwacje. Szczegóły produktu, koszyk i ulubione zwracają `quantityAvailable`, administracja także `quantityReserved`. `ReservationExpiryWorker` utrwala wygaśnięcia; termin działa również przed jego wykonaniem. Serwerowe `ConfirmPaymentAsync` rozlicza stan raz albo zapisuje `PaymentReview`. Migracja pozostaje lokalna; Tpay, dostawa i pełny checkout są osobnym zakresem. Reguły i koszt wspólnej blokady katalogu opisuje [inventory-model.md](inventory-model.md).

Administracja ofertą (01.11.2026): kontrolery `AdminProducts`, `AdminVariants`, `AdminCategories`, `AdminAttributes` i `AdminVariantStock` korzystają ze wspólnego dostępu administratora i serwisów w `Services/Catalog/Management/`. `CatalogCommandStore` utrwala komendę wraz z wynikiem w jednej transakcji; obsługuje również odroczone błędy przy commit. Wersje opisowe produktu/wariantu i ich relacji generuje PostgreSQL, cena i magazyn zachowują odrębną kontrolę zmian. Odczyt administracyjny używa spójnego snapshotu bez globalnej blokady piszących. Operacje publikacji wymagają kategorii, opublikowanego wariantu i bieżącej ceny PLN; ukrycie zachowuje dane zakupowe i historię. Nowe migracje zastosowano lokalnie, bez Supabase. [Zakres i instrukcja](catalog-management-implementation-plan.md#13-wdrożenie-odbiór-lokalny-i-użycie).

- Repozytorium zawiera API ASP.NET Core na .NET 10. `Program.cs` rejestruje kontrolery, klienta HTTP Supabase Auth, ASP.NET Core Data Protection i `AppDbContext` z Npgsql.
- `Program.cs` generuje dokument OpenAPI z endpointów oraz udostępnia Swagger UI w środowisku `Development` pod `/swagger`; JSON jest pod `/openapi/v1.json`.
- `Controllers/WebApiControllerBase.cs` ustawia prefiks tras `api/v1/web/[controller]` dla kontrolerów, które po nim dziedziczą. Konwencja w `Program.cs` zamienia token nazwy kontrolera na małe litery, także w generowanym OpenAPI. `Controllers/AuthController.cs` obsługuje rejestrację, logowanie, odczyt bieżącego użytkownika i wylogowanie. `Controllers/ProfileController.cs` obsługuje profil zalogowanego użytkownika. `Controllers/CategoriesController.cs` zwraca całe drzewo kategorii, gałąź wskazaną przez `rootId` albo dane jednej kategorii po UUID. `Controllers/ProductsController.cs` zwraca szczegóły opublikowanego produktu, proste listy GET oraz zaawansowane wyszukiwanie `POST /products/search` z filtrami w body JSON. `PasswordResetController` deklaruje trasę `api/v1/web/auth/password-reset` bezpośrednio.
- `Contracts/` zawiera modele wejściowe, `Services/Auth/` komunikację z Supabase Auth i obsługę ciasteczka, `Services/Profile/` odczyt profilu, `Entities/` modele danych, a `Data/` mapowanie EF Core i migracje.
- `Services/Pricing/VariantPriceService` dopisuje zwykłą cenę PLN, serializuje zapisy i rozpoznaje ponowione operacje. `AdminVariantPricesController` wystawia cenę bieżącą, zapis i historię pod `/api/v1/web/admin/variants/{id}`. `IAdministratorAccess` sprawdza sesję przez Supabase Auth i aktualny wpis w `app.administrators`; kontroler przekazuje do serwisu potwierdzone ID autora. Zapis wymaga także nagłówka CSRF. Szczegóły opisują [model cen](pricing-model.md) i [dostęp administratora](administrator-access.md).
- Listy produktów projektują tylko pola kafelka, pobierają warianty i ich ostatnie obowiązujące ceny osobnymi zapytaniami; szczegóły nadal buduje `ProductDetailsBuilder`. `GET /products/filter-options` wyznacza dostępne pary cech z widocznych wariantów, a odczyt pojedynczej kategorii dodaje ścieżkę od korzenia.
- `POST /products/search` używa wspólnej konstrukcji zapytania po widocznych, wycenionych wariantach do filtrowania produktów i wyliczania kontekstowych opcji cech. Liczniki agreguje baza po różnych produktach; dla wybranego klucza pomijany jest jego własny filtr.
- `SupabaseAuthClient` wywołuje Supabase Auth przez HTTP. Supabase tworzy konto, sprawdza hasło, wystawia tokeny i zwraca dane uwierzytelnionego użytkownika. Backend nie przechowuje haseł ani nie wystawia własnych JWT.
- `PasswordResetController` używa osobnego klienta z tajnym kluczem Supabase tylko do wygenerowania linku odzyskiwania. `MailtrapEmailSender` wysyła wiadomość przez Mailtrap Transactional API z adresem i nazwą nadawcy oraz kategorią wiadomości; nie wymaga SDK. Weryfikacja tokenu i zmiana hasła przechodzą przez klienta Supabase z kluczem publikowalnym i tokenem użytkownika. Backend nie przechowuje tokenów resetu.
- Po logowaniu `AuthCookieService` zapisuje otrzymane access token i refresh token wraz ze stałym terminem sesji w jednej wartości chronionej przez Data Protection. Przeglądarka otrzymuje ciasteczko `HttpOnly; Secure; SameSite=Lax`, bez tokenów w JSON. Bez `rememberMe` termin wynosi 1 dzień od logowania, z `rememberMe` 30 dni; odświeżenie tokenów go nie przesuwa.
- Przy chronionym żądaniu `AuthenticatedUserService` odczytuje ciasteczko i sprawdza użytkownika przez Supabase Auth. Po odrzuceniu access tokenu próbuje wymienić parę tokenów i ponawia sprawdzenie. Dopiero zweryfikowany identyfikator użytkownika jest używany do odczytu jego danych z PostgreSQL.
- `AppDbContext` mapuje `UserProfile` do `app.user_profiles`. `auth.users` należy do Supabase; EF tylko odwołuje się do jej `id` jako klucza obcego. Schematy i uprawnienia opisuje [database.md](database.md).
- `AppDbContext` mapuje też katalog, kategorie, stan wariantu, historię cen i administratorów do schematu `app`. Migracje są zastosowane w Supabase. API ma odczyt katalogu i kolekcji oraz administracyjny zapis cen PLN, kolekcji i promocji; tworzenie i edycja produktów, wariantów i kategorii pozostają do wdrożenia.
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

## Serwisy promocji — 31.10.2026

`AdminPromotionsController` używa istniejącego `IAdministratorAccess`, a po jego sprawdzeniu wywołuje scoped `PromotionService`. Kalkulatory `PromotionCalculator` i `PriorPriceCalculator` nie wykonują IO. Oferta jest rejestrowana triggerami PostgreSQL, ponieważ katalog nie ma jeszcze endpointów tworzenia/edycji. Wspólna blokada zapisu katalogu poprzedza blokady zwykłej ceny i jest również stosowana przez seeder; szczegóły i koszt serializacji opisuje [plan promocji](promotion-implementation-plan.md).

`PublicPriceBuilder` buduje wspólne pola ceny z zapisanego zdarzenia obniżki. Szczegóły produktu i kafelek używają tych samych reguł; kafelek wybiera cenę jednego pasującego wariantu per waluta. `ProductsController` nadal filtruje i sortuje po obowiązującym `selling_amount`. Dane administratora nie trafiają do publicznego DTO. Migracja promocji jest zastosowana w Supabase; operacje zapisu produktów i kategorii nadal są osobną pracą.

## Kolekcje — 01.11.2026

`CollectionService` obsługuje metadane, skład, wersje i ponowienia; `AdminCollectionsController` korzysta z istniejącego `IAdministratorAccess`. `CollectionsController` udostępnia metadane publicznych kolekcji. `CollectionScope` wspólnie rozwiązuje id/slug i zawęża istniejące zapytania `ProductsController`; listy, ceny i liczniki filtrów nie mają drugiej skopiowanej implementacji. Kategoria i kolekcja przecinają swoje zakresy.

`PromotionService` rozwiązuje teraz cel product/collection, a dalej korzysta ze wspólnego kalkulatora i atomowych zapisów. Historię cen i oferty ładuje zbiorczo, bez osobnego wywołania per produkt. Start kolekcji sprawdza jej wersję i utrwala warianty; koniec używa zapisanych udziałów niezależnie od aktualnej kolekcji. Wersje kolekcji są nadawane przez PostgreSQL, także przy SQL. Wspólna blokada katalogu obejmuje jej zapisy. Osobny `CollectionOperation` przechowuje ponowienia komend kolekcji. Stary fingerprint produktu zachowano; nowy format ma prefiks `collection/v1:`.

## Dynamiczne promocje — 02.11.2026

`DynamicPromotionService` obsługuje nowe scope catalog/category, token podglądu, wykluczenia i stronicowane odczyty konfliktów/udziałów. `DynamicPromotionEngine` wykonuje najpierw wyjścia, potem wejścia wariantów i aktualizuje diagnostykę. Komendy katalogu, cen i promocji wywołują go przed commit pod CatalogWriteLock. Dotychczasowe promocje produktu/kolekcji zachowują skład i format ponowień.

`PromotionReconciliationWorker` tworzy nowy scope DI co 5 sekund i sprawdza trwały znacznik; pełną ocenę powtarza po minucie również bez nowej zmiany danych. Przy awarii znacznika nie usuwa, a wieloinstancyjne wykonanie jest serializowane w bazie. Bezpośredni SQL ma opóźnione przeliczenie przez worker; API zapisuje i przelicza w jednej transakcji. Rozszerzenie nie dodaje harmonogramu kampanii ani integracji zewnętrznej. [Pełne reguły](promotion-extension-plan.md).
