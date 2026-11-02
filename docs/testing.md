# Testy backendu

Cel: opisać układ testów i sposób ich uruchamiania w tym repozytorium.

## Magazyn i minimalne zamówienie — 01.11.2026

Weryfikacja lokalna: `tests/inventory/verify-local.ps1`. Skrypt tworzy własny PostgreSQL 17 na wolnym porcie i bazę `ecomm_test_inventory`, przygotowuje minimalne lokalne referencje Auth, buduje Release, stosuje migracje od zera i uruchamia cały zestaw xUnit. Nie tworzy kont w Supabase i nie używa zewnętrznego Auth; HTTP korzysta z istniejącej atrapy. Na końcu zatrzymuje własny PostgreSQL i odtwarza zmienną środowiskową. Logi i TRX są w `.local/test-results/inventory-*`.

`InventoryPostgresTests` i `InventoryHttpTests` dodają 13 scenariuszy: ostatnia sztuka, równoczesne ponowienia bez przedłużania terminu, akceptacja ceny, wielopozycyjny rollback i błąd bazy przy faktycznym commit po opróżnieniu koszyka, rozliczenie raz, kontrola kwoty/zdarzenia, wygasanie bez workera, spóźniona płatność po rezerwacji innego klienta, wyścig płatności z anulowaniem, korekta administratora i SQL poniżej rezerwacji, blokada seedera, niezmienność historii, dwa rzeczywiście uruchomione workery, brak/zero stanu i ukrycie, cena zerowa i snapshot po zmianie ceny. HTTP sprawdza konto, właściciela A/B, CSRF, idempotencję, nieznane pola, 400/415, no-store, paginację, prawdziwy middleware 429 i awarię Auth bez fallbacku. Nie istnieje endpoint klienta potwierdzający płatność.

Symulacja upływu czasu w fixture zmienia termin wyłącznie uprzywilejowanym SQL w izolowanej bazie i transakcyjnie przywraca trigger tożsamości po wykonaniu odroczonych sprawdzeń. Aplikacja nie ma przełącznika obchodzącego termin. Cleanup testów jawnie czyści sześć tabel zakupowych; seeder aplikacji odmawia takiego resetu przy zamówieniach.

Wynik końcowy: **175/175 testów, 0 pominiętych; build Release: 0 błędów i ostrzeżeń**. Skrypt potwierdził migrację od zera, Down/Up przy pustych tabelach zakupowych i istniejącym katalogu, identyczny skrót wszystkich pól historii cen oraz brak różnic modelu EF. Cofnięcie przy rzeczywistych zamówieniach jest blokowane w migracji. Ponowny odbiór 02.11.2026: również 175/175. Raport ostatniego przebiegu: `.local/test-results/inventory-20261102-113016-49d74577/inventory.trx` oraz sąsiednie logi. Własny PostgreSQL został zatrzymany po odbiorze.

Granica dowodu: lokalny PostgreSQL i rzeczywisty pipeline HTTP z atrapą zewnętrznego Auth. Nie zastosowano nowej migracji w Supabase, nie odebrano frontendu ani Tpay, nie sprawdzono rzeczywistych powiadomień płatności i nie wykonano pomiaru przepustowości globalnej blokady magazynu. [Reguły i granice](inventory-model.md).

## Koszyk — odbiór HTTPS z rzeczywistym Auth (01.11.2026)

Wynik: **54/54 sprawdzenia zaliczone**, przez uruchomioną aplikację ASP.NET Core, lokalne HTTPS i rzeczywisty Supabase Auth, bez atrapy Auth. Dwa tymczasowe konta utworzono przez Admin API z `email_confirm=true`, bez wysyłania wiadomości i bez używania istniejących haseł klientów. Katalog, kopie UUID kont dla FK oraz zmiany ceny, magazynu, widoczności i terminu gościa istniały wyłącznie w izolowanym PostgreSQL `ecomm_test_shopping_live`. Nie stosowano migracji ani nie zmieniano katalogu w zdalnym Supabase.

Sprawdzono: token gościa i flagi cookie, brak rezerwacji, ilości/limity, CSRF i niedozwolone pola JSON, brak przedłużania terminu gościa, logowanie prawdziwych kont A/B, izolację od gościa i między kontami, ulubione dokładnego wariantu i deduplikację, paginację, dwie niezależne sesje jednego konta, konflikt wersji między sesjami, lokalne wylogowanie bez unieważnienia drugiej sesji, powrót gościa i ponowne logowanie bez scalania. Dalej: bieżącą zmianę ceny, brak towaru i sumę dostępnych pozycji, zapis niedostępnego wariantu do ulubionych, ukrycie i redakcję danych, zmniejszanie/usuwanie pozycji niedostępnych, czyszczenie koszyka, wymuszone w lokalnym fixture wygaśnięcie gościa, nową sesję i jej unieważnienie oraz brak endpointu importu.

Po przebiegu wylogowano wszystkie sesje, usunięto obydwa konta z Supabase Auth i zweryfikowano `404` przy ich odczycie; usunięto też lokalne referencje kont. API i tymczasowy PostgreSQL zostały zatrzymane. Build przed odbiorem: 0 błędów/ostrzeżeń. Raport bez haseł, tokenów i kluczy: `.local/test-results/shopping-live-20261101-150334-202554ff/report.json`.

Odtworzenie całej próby:

```powershell
./tests/smoke/run-shopping-local.ps1
```

Wymagane: PostgreSQL 17 w domyślnej lokalizacji Windows (albo parametr `-PgBin`), SDK .NET, developerski certyfikat HTTPS i komplet konfiguracji startowej API w user-secrets, w tym rzeczywiste klucze Supabase. Launcher wybiera wolne porty, tworzy własny lokalny klaster i bazę ICU, stosuje lokalnie migracje, uruchamia API bez otwierania okna/podglądu i wykonuje [shopping.ps1](../tests/smoke/shopping.ps1). Przy zakończeniu zatrzymuje własne procesy i odtwarza zmienne środowiskowe. **Próba tworzy i usuwa dwa tymczasowe konta w skonfigurowanym projekcie Auth**; nie wysyła e-maili. Tajny klucz pozostaje po stronie procesu narzędzia, nie trafia do raportu ani klienta.

Sam `shopping.ps1` można uruchomić względem przygotowanej instancji:

```powershell
./tests/smoke/shopping.ps1 -Api 'https://localhost:15476/api/v1/web' `
  -Database ecomm_test_shopping_http -PgPort 15474 -ReportPath .local/shopping-live.json
```

Wymaga zgodnego `ECOMMERCE_SMOKE_SEED_KEY`. Przed resetem odczytuje przez API losowy rekord wstawiony do podanej lokalnej bazy. Przyjmuje tylko HTTPS localhost i bazę `ecomm_test_*`. Walidacja certyfikatu jest pomijana wyłącznie dla lokalnego API, a dla Supabase pozostaje standardowa. Konta są sprzątane w `finally`, także po błędzie asercji; ewentualne nieusunięte UUID są wskazane w raporcie i ostrzeżeniu. Helper czyści nagłówki sesji przed każdym żądaniem, zachowując cookies, ponieważ [PowerShell przechowuje przekazane nagłówki w WebSession](https://raw.githubusercontent.com/PowerShell/PowerShell/master/src/Microsoft.PowerShell.Commands.Utility/commands/utility/WebCmdlet/Common/WebRequestPSCmdlet.Common.cs); bez tego próba „bez CSRF” mogłaby nadal wysyłać poprzedni nagłówek.

Granica dowodu: prawdziwe Auth i transport HTTPS z lokalną bazą aplikacji, nie zdalny katalog Supabase ani frontend. Sprawdzony lokalny frontend `D:/CODE/E-comm/Frontend/ecommerce-web-front` ma stronę główną i design system, bez ekranów i integracji koszyka/ulubionych. Nie ma więc jeszcze ścieżki UI do odbioru; domeny, CORS/SameSite przyszłego połączenia i migracja zdalnej bazy pozostają otwarte.

## Zarządzanie ofertą — odbiór lokalny 01.11.2026

Końcowy wynik całego rozwiązania: **162/162 testy, bez pominięć; build 0 błędów i ostrzeżeń**. Testy uruchomiono na własnym PostgreSQL 17, niezależnie od procesu i bazy drugiego agenta. Skrypt `tests/performance/catalog-management/verify-migrations.ps1` potwierdził aktualizację istniejącego schematu, odrzucenie niezgodnych danych, identyczny skrót historii cen, zachowanie nieznanej daty publikacji także po edycji metadanych oraz Down/Up migracji publikacji.

`CatalogManagementInputTests` sprawdza slugi, NFC/trim bez lowercase, adresy zdjęć i odrzucanie pól serwerowych. `CatalogManagementPostgresTests` sprawdza tworzenie grafu, wersje i ich propagację, niezależność ceny/magazynu, współbieżne edycje i SKU, duplikaty konfiguracji, słownik, cykle kategorii, audyt i ponowienia także po usunięciu kategorii. Osobno potwierdza rollback produktu i dziennika przy odroczonym błędzie commit oraz brak uprawnień ról Data API do dziennika.

`CatalogManagementHttpTests` przechodzi przez rzeczywisty routing/MVC/JSON i cookies aplikacji, ze stubem zewnętrznego Supabase Auth oraz prawdziwą tabelą administratorów w lokalnym PostgreSQL. Sprawdza brak dostępu, CSRF, odwołanie administratora, `no-store` także dla automatycznego `400`, DELETE z wersją query i pełny proces publikacji. Łączy ofertę z koszykiem i ulubionymi: ukrycie pozostawia pozycje, ale odczyt nie ujawnia danych niedostępnego wariantu.

Nowy zestaw to 20 przypadków; używa istniejącej kolekcji `Postgres catalog` i lokalnej bazy `ecomm_test_*` po obu migracjach oferty. Dawne scenariusze USD/przyszłej ceny zachowują dane historyczne przez testowy `LegacyCatalogFixture`; jego uprzywilejowane wyłączenie nowych ograniczeń istnieje wyłącznie w fixture. API nie ma przełącznika obchodzącego publikację. Test koszyka ukrywa najpierw produkt, zanim ukryje jego jedyny wariant.

Przy równoległej pracy agentów budować do osobnego katalogu, aby nie blokować plików DLL drugiego testhosta:

```powershell
dotnet build E-commerce-backend.slnx --artifacts-path "$env:TEMP/ecomm-offer-artifacts"
dotnet test E-commerce-backend.slnx --artifacts-path "$env:TEMP/ecomm-offer-artifacts"
```

Wymagane `ECOMMERCE_TEST_DATABASE` jak w dalszych instrukcjach. Weryfikacja migracji: nowa baza od zera przeszła, a [raport przed wdrożeniem](../scripts/catalog-preflight.sql) nie wykazał problemów na syntetycznym zbiorze 5001 produktów. Odczytowy raport skonfigurowanego Supabase dał 0 blokad i 0 ostrzeżeń; zastosowanie obu migracji pozostaje osobnym krokiem. Wynik pomiaru jest w [audycie](catalog-performance-audit.md#administracja-ofertą--pomiar-01112026).

## Koszyk i ulubione — weryfikacja 01.11.2026

Najnowszy odbiór bez atrapy Supabase Auth opisuje [sekcja HTTPS](#koszyk--odbiór-https-z-rzeczywistym-auth-01112026). Poniższe testy xUnit nadal używają atrap zewnętrznych odpowiedzi Auth.

`Integration/ShoppingPostgresTests.cs` i `Integration/ShoppingHttpTests.cs` obejmują łącznie 10 scenariuszy na izolowanej bazie `ecomm_test_*`. Używają kolekcji `Postgres catalog`; nie wolno uruchamiać ich na danych sklepu. Fixture tworzy dwa lokalne UUID w minimalnym `auth.users`, bez profili; nie tworzy kont w Supabase. HTTP korzysta z `WebApplicationFactory<Program>`, rzeczywistego pipeline MVC, cookies i rate limitera oraz atrapy Supabase Auth. Żadne żądanie Auth z tych testów nie trafia do zewnętrznej usługi.

Zakres: osobne koszyki gościa/A/B bez scalania, powrót gościa po logout, ulubione wariantu tylko na koncie, dwa warianty jednego produktu, deduplikacja, brak profilu, usunięcie konta, limity 100/99/500, paginacja, wygasanie/revokacja tokenu gościa, brak przedłużania terminu, brak rezerwacji, bieżące i przyszłe ceny, promocja kolekcji identyczna w katalogu/koszyku/ulubionych, nieznany stan, brak towaru i placeholder ukrytej oferty. Zapisy sprawdzają wersję i zakres, także przy równych numerach wersji dwóch właścicieli. Równoczesne komendy mają jednego zwycięzcę; reset konkurujący z zapisem zachowuje właścicieli i usuwa stare pozycje.

Testy HTTP sprawdzają PUT/DELETE i warunki w query, brak importu, odrzucanie gościa na ulubionych, nieznane pola JSON, `400`/`403`/`415`, `no-store`, nieważny Auth i awarię bez fallbacku do gościa oraz rzeczywisty `429`. To nie jest odbiór przeglądarkowy ani próba prawdziwego Supabase Auth.

Weryfikacja zakresu koszyka na bazie HEAD `f316b7b` z tym wdrożeniem, w osobnym worktree: `dotnet build` — 0 ostrzeżeń i błędów; pełny zestaw **142/142**, bez pominięć. Migrację `AddShoppingCartsAndFavorites` zastosowano od zera oraz cofnięto i ponownie zastosowano, zachowując 4 istniejące rekordy historii cen; brak rozbieżności modelu EF ze snapshotem. Migracja i testy wykonywały zapis tylko lokalnie, nie w Supabase.

Lokalna baza powinna używać kolacji językowej, np. ICU `en-US`. Kolacja `C` powoduje, że istniejący test sortowania nazw (`Zulu` vs `needle`) ma inną kolejność — nie jest to regresja zapytania koszyka. Przykład utworzenia pustej bazy na PostgreSQL z ICU:

```sql
CREATE DATABASE ecomm_test_catalog TEMPLATE template0 ENCODING 'UTF8'
    LOCALE_PROVIDER icu ICU_LOCALE 'en-US';
```

Po bootstrapie `auth` i zastosowaniu migracji:

```powershell
dotnet test E-commerce-backend.slnx --filter FullyQualifiedName~Shopping
```

Ustawienie `ECOMMERCE_TEST_DATABASE` i zasady izolacji opisuje następna sekcja. Do zakończenia odbioru pozostają frontend, docelowe domeny i uzgodnione zastosowanie migracji w Supabase; sama kompilacja lub pominięte testy PostgreSQL nie zamykają tych punktów.

Przed commitem wydzielono wyłącznie koszyk i ulubione w osobnym worktree, bez administracji ofertą. Ponowna weryfikacja: build 0 błędów/ostrzeżeń, brak różnic modelu EF i pełne **142/142 testy, 0 pominiętych**, na świeżym lokalnym PostgreSQL 17 z ICU en-US. Raport: `.local/test-results/shopping-commit-20261101-151034/results.trx`. Tymczasowy PostgreSQL zatrzymano po próbie.

## Integracyjne testy katalogu na PostgreSQL

`AdminPriceEndpointTests` sprawdza odrzucenie braku sesji/uprawnienia/niedostępności Auth przed dostępem do cen, nagłówek CSRF, klucz operacji, dane i paginację oraz odrzucanie pól kontrolowanych przez serwer. `AdminPricePostgresTests` korzysta z prawdziwego PostgreSQL i atrapy Supabase Auth: sprawdza brak zaufania do `user_metadata`, nadanie/odebranie uprawnienia, zachowanie administratora przy resecie katalogu i FK do konta, zmianę ceny z autorem z sesji, ponowienia, konflikty, historię i odczyt produktu po zmianie. Wymaga migracji `AddAdministrators`; należy do kolekcji `Postgres catalog`. Nie zastępuje próby z dwoma prawdziwymi kontami.

Weryfikacja z 31.10.2026: build bez ostrzeżeń, 90/90 testów z lokalnym PostgreSQL, bez pominięć. Próba HTTP na uruchomionym API potwierdziła obie ścieżki i trzy operacje w OpenAPI, `401` i `no-store` dla anonimowych odczytów, `401` dla anonimowego zapisu oraz `400` po dodaniu niedozwolonego pola `actorUserId` w JSON (walidacja przez rzeczywisty pipeline MVC).

`Integration/PriceChangePostgresTests.cs` sprawdza serwis dopisywania cen: pierwszą cenę i aktualizację, wersje, ponowienie po kolejnej zmianie, równoczesne pierwsze zapisy i ponowienia, konflikt klucza między wariantami, istniejący rabat, przyszłą cenę i kolizję czasu. Sprawdza również rzeczywiste blokady `UPDATE`/`DELETE`, unikalny indeks operacji oraz reset seedera po zapisaniu cen. Wymaga migracji `AddPriceChangeOperations`. Obie klasy integracyjne korzystają z kolekcji `Postgres catalog`, aby ich destrukcyjne seedy nie działały równolegle. Test jednostkowy `VariantPriceServiceTests` sprawdza walidację przed otwarciem połączenia z bazą.

`Integration/ProductSearchPostgresTests.cs` uruchamia rzeczywiste zapytania EF Core na lokalnym PostgreSQL. Sprawdza dopasowanie cech i ceny do tego samego wariantu, liczenie produktu raz przy kilku pasujących wariantach, alternatywy i zera filtrów, przyszłą cenę, brak ceny PLN oraz kolejność sortowania. Osobny scenariusz w tej klasie sprawdza sześć poziomów rankingu `q`, remisy po nazwie, stronicowanie, jawne `sort`, ranking po odfiltrowaniu droższego wariantu, dosłowne znaki specjalne i te same wyniki w trzech trasach. Test uruchamia seeder, który czyści czternaście tabel katalogu, dlatego wymaga osobnej bazy o nazwie zaczynającej się od `ecomm_test_` na `localhost` albo `127.0.0.1`. Gdy zmienna nie jest ustawiona, xUnit oznacza test jako pominięty.

Po utworzeniu osobnej bazy i zastosowaniu migracji uruchom (na zwykłym PostgreSQL najpierw przygotuj minimalny schemat `auth` i role skryptem `tests/performance/bootstrap-local-postgres.sql`):

```powershell
$env:ECOMMERCE_TEST_DATABASE = 'Host=127.0.0.1;Port=5432;Database=ecomm_test_catalog;Username=postgres;Password=...'
dotnet test E-commerce-backend.slnx --filter FullyQualifiedName~ProductSearchPostgresTests
```

Zestaw pomiarowy dla 5000 produktów opisuje [audyt katalogu](catalog-performance-audit.md) i skrypty w `tests/performance/`.

Próba odczytu z 28.10.2026 na skonfigurowanej bazie sprawdziła: lekkie `items` i `pricesFrom` w katalogu (262 produkty), opcje cech dla `demo-odziez`, filtrowanie POST po cesze z `sort=newest`, `sort=name-desc` w GET, ścieżkę kategorii oraz niezmieniony pełny odczyt szczegółów. Kolejna próba POST sprawdziła kontekstowe `availableFilters`: bez filtrów, dla pasującej pary `kolor=czerwony` i `rozmiar=S`, dla kombinacji bez wyników (`czerwony` + `M`), dla `q` trafiającego w SKU innego wariantu, stałość liczników między stronami oraz zachowanie nieznanej wybranej wartości. Przy zerowym wyniku wybrana wartość pozostała z licznikiem `0`, a alternatywna wartość dostała licznik `1`. Testy jednostkowe obejmują ścieżkę od korzenia, odrzucanie błędnego `sort` i selektorów kategorii oraz składanie filtrów z wybranymi wartościami o liczniku `0`. Te próby nie zastępują automatycznego testu integracyjnego z PostgreSQL dla widoczności i historii cen.

Po dodaniu odczytu cenowego ręczne próby HTTP na skonfigurowanym PostgreSQL sprawdziły `price-asc` i `price-desc`, oba końce zakresu oraz pojedyncze granice, granicę równą 90 PLN, wspólne dopasowanie SKU i ceny na jednym wariancie, liczniki `availableFilters` przy zakresie ceny, brak nakładania się dwóch stron oraz `400` dla ceny ujemnej, odwróconego zakresu i nadmiernej precyzji. Test jednostkowy w `ProductsEndpointTests.cs` sprawdza walidację zakresu przed dostępem do bazy. Automatyczny test integracyjny sprawdza przyszły wpis historii ceny i brak PLN; scenariusze współbieżnego zapisu pozostają do dodania.

## Stan potwierdzony

- Rozwiązanie `E-commerce-backend.slnx` zawiera aplikację API oraz projekt xUnit `tests/E-commerce-backend.Tests`.
- Projekt API wyklucza cały katalog `tests/` ze swoich elementów kompilacji i zawartości, ponieważ leży w katalogu głównym repozytorium.
- Projekt testowy zawiera xUnit, uruchamiany poleceniem `dotnet test E-commerce-backend.slnx`.
- `Examples/Calculator.cs` zawiera demonstracyjną funkcję `Add`. `Unit/CalculatorTests.cs` testuje ją bez uruchamiania API.
- `Unit/AuthLoginTests.cs` sprawdza wystawienie chronionego ciasteczka, oba terminy sesji, limit rozmiaru i odczyt po ponownym utworzeniu Data Protection. `Unit/AuthEndpointTests.cs` sprawdza zachowanie `/me` i `/logout` z atrapą odpowiedzi Supabase, w tym odświeżenie pary tokenów bez przesunięcia terminu, błędy usługi, nieważny refresh token i dwa równoległe żądania. Testy nie wysyłają żądań do prawdziwego projektu Supabase.
- `Unit/AuthLoginTests.cs` sprawdza także mapowanie rzeczywistego formatu odpowiedzi `invalid_credentials`; `Unit/AuthRegisterErrorTests.cs` pilnuje tego samego formatu dla `weak_password` przy rejestracji.
- `Unit/AuthEndpointTests.cs` sprawdza również `firstName` i `lastName` w `/auth/me`: `null` przed utworzeniem profilu, zapisane wartości po jego utworzeniu oraz brak ujawnienia profilu innego UUID. Odczyt profilu jest w testach atrapą; nie sprawdzają one połączenia z prawdziwą bazą.
- `Unit/UserProfileModelTests.cs` sprawdza mapowanie klucza głównego i obcego profilu do `auth.users`. Wygenerowany skrypt migracji został sprawdzony lokalnie; testy nie zastępują sprawdzenia migracji i uprawnień w prawdziwej bazie.
- `Unit/CatalogModelTests.cs` sprawdza mapowanie relacji wariantu, ceny i stanu, indeksy kategorii, klucze tabel łączących oraz normalizację kodu cechy. Testy metadanych nie wykonują wyzwalaczy migracji; wymagają one osobnej próby na PostgreSQL.
- `Unit/CategoryTreeBuilderTests.cs` sprawdza budowę pełnego drzewa, odcięcie gałęzi od kategorii pośredniej, zestaw ID kategorii z jej potomkami, pusty katalog i nieznane ID. Ręczna próba HTTP 22.10.2026 na lokalnie uruchomionym API i skonfigurowanej bazie potwierdziła odczyt drzewa, poddrzewa i danych pojedynczej kategorii oraz odpowiedzi `400` i `404`. Nadal brakuje automatycznego testu integracyjnego z PostgreSQL.
- `Unit/ProductDetailsBuilderTests.cs` sprawdza filtrowanie ukrytych i pozbawionych ceny wariantów, wybór bieżącej ceny dla waluty, pomijanie ceny przyszłej, cechy i rozróżnienie nieznanego stanu od zera. `Unit/ProductsEndpointTests.cs` sprawdza odrzucenie braku parametru, obu parametrów oraz pustego `slug` dla produktu i kategorii, a także błędnych wartości stronicowania obu list przed dostępem do bazy. Ręczne próby HTTP 22.10.2026 na lokalnie uruchomionym API i skonfigurowanej bazie potwierdziły odczyt tego samego zasianego produktu przez `?id=` i `?slug=`, a także 251 unikalnych produktów z kategorii głównej przez `?categoryId=` i `?slug=`. Opcjonalne stronicowanie listy kategorii dało po 20 produktów na stronach 1 i 2 bez nakładania ID, 11 na stronie 13 i pustą stronę 14; brak `pageSize` zastosował wartość 20, a błędne parametry dały `400`. Po zmianie formatu odpowiedzi ręczny odczyt potwierdził `items` z 251 produktami i `pagination=null` bez parametrów, a na stronie 2: 20 produktów, `totalItems=251`, `totalPages=13` i poprawne znaczniki sąsiednich stron. Ogólna lista `/products/catalog` zwróciła 262 widoczne produkty, czyli więcej niż jedna kategoria główna, a strony 1 i 2 po 20 elementów bez powtórzonych ID; `totalItems=262`, `totalPages=14`. Błędne wartości paginacji dały `400`; ponowny odczyt szczegółów po slugu oraz listy z kategorii potwierdził, że wcześniejsze trasy nadal działają. Sprawdzono też odpowiedzi `400` dla braku lub obu parametrów kategorii i błędnego UUID oraz `404` dla nieznanego slugu. Seeder nie tworzy powtórzonych slugów kategorii, więc `409` nie było objęte ręczną próbą. Nadal brakuje automatycznego testu integracyjnego z PostgreSQL.
- `Unit/DemoSeedAccessTests.cs` sprawdza rozpoznawanie poprawnego i błędnego klucza endpointu seedującego. `Unit/DemoSeedOptionsTests.cs` sprawdza zakresy domyślnych i niezależnych poziomów, granice losowania oraz odrzucanie niepoprawnych wartości. `Unit/DemoCatalogSeedDataTests.cs` sprawdza liczniki, relacje i unikalność zestawów wariantów dla zestawu bazowego i największego. Reset czternastu tabel i zapis sprawdzają testy na izolowanym PostgreSQL; instrukcja jest w [demo-seed.md](demo-seed.md).
- `Unit/ProfileEndpointTests.cs` sprawdza, że odczyt i zapis profilu bez sesji kończą się przed wywołaniem Supabase lub PostgreSQL, a zapis wymaga nagłówka chroniącego przed prostym żądaniem CSRF.
- `Unit/PasswordResetTests.cs` sprawdza wysłanie linku, nieujawnianie braku konta, kolejność weryfikacji tokenu i zmiany hasła oraz format żądania do Mailtrap. Testy używają atrap HTTP; nie potwierdzają działania konta Mailtrap ani ustawień Supabase.
- Funkcja demonstracyjna nie jest używana przez endpoint `GET /` i nie stanowi logiki biznesowej sklepu.

## Wyszukiwanie produktów

Wyszukiwanie `q` sprawdzono ręcznie 23.10.2026 przez HTTP na skonfigurowanej bazie: fraza `czapka` i SKU `demo-cap-one` zwróciły po jednym produkcie w katalogu, a SKU także jeden produkt w `demo-akcesoria` i zero w `demo-odziez`. Przy stronicowaniu `totalItems` odzwierciedla wynik wyszukiwania. Puste `q` zachowało 262 produkty; dosłowne `%` i `_` nie zadziałały jak wieloznaczniki; 101 znaków dało `400`. Test jednostkowy sprawdza odrzucanie zbyt długiego `q` przed dostępem do bazy. Automatyczny test integracyjny wyszukiwania i rankingu działa na izolowanym PostgreSQL.

Filtry cech sprawdzono ręcznie 23.10.2026 przez HTTP na skonfigurowanej bazie we wcześniejszym formacie query. Następnie przeniesiono je do `POST /api/v1/web/products/search` z body JSON. Próba HTTP 26.10.2026 potwierdziła, że `demo-kolor=czerwony` i `demo-rozmiar=S` wraz z `q=Koszulka` dają jeden produkt, zaś po zamianie rozmiaru na `M` — zero, co potwierdza dopasowanie w obrębie jednego wariantu. SKU innego wariantu w `q` również daje zero. Filtr z `categorySlug=demo-akcesoria` zwrócił jeden produkt. Dwie wartości tego samego klucza działały jako alternatywa. Puste body `{}` zwróciło stronę 1 po 20 pozycji z `totalItems=262`. Po przebudowaniu API prosty GET z `q=Czapka` zwrócił jeden produkt, stary filtr cech w query dostał `400`, a nieznana `categorySlug` w POST dała `404`. Testy jednostkowe sprawdzają odrzucanie błędnego body i dawnego filtra w GET przed dostępem do bazy; automatyczny test integracyjny PostgreSQL obejmuje dopasowanie cech i liczniki filtrów.

## Zasada na przyszłość

Gdy pojawią się reguły biznesowe, dodajemy ich testy jednostkowe do projektu testowego. Test powinien wywoływać kod bez uruchamiania serwera HTTP i bez zależności od bazy danych lub innych usług.

Gdy AI tworzy testy, powinno sprawdzać zarówno poprawne wyniki, jak i oczekiwane niepowodzenia oraz istotne przypadki brzegowe. Dla scenariusza błędnego należy potwierdzić konkretne zachowanie wynikające z kontraktu kodu, np. właściwy wyjątek lub wynik oznaczający błąd. Nie ograniczamy zestawu testów do ścieżki pomyślnej i nie wymyślamy zachowań błędnych, których aplikacja nie definiuje.

## Promocje — weryfikacja 31.10.2026

`Unit/PromotionCalculatorTests.cs` obejmuje rabaty procentowe/kwotowe, zero, grosze, limity, okres przed oknem, przerwy w ofercie, krótką/nieznaną historię i DST Warszawy. `Unit/PromotionEndpointTests.cs` sprawdza dostęp dla wszystkich siedmiu tras przed IO, nagłówki zapisu i odrzucanie pól serwerowych.

`Integration/PromotionPostgresTests.cs` używa tej samej izolowanej bazy `ECOMMERCE_TEST_DATABASE` (`localhost`, nazwa `ecomm_test_*`). Obejmuje pełny cykl, wszystkie publiczne ścieżki odczytu i zachowanie zapisanego odniesienia po 31 dniach, równoczesne starty, start kontra zwykła cena, wersje, ponowienia, brak zmiany składu po późniejszej publikacji, zakończenie po ukryciu, indeks aktywnego udziału, niezmienność odniesienia, reset oraz triggery widoczności/cenę przyszłą. Tymczasowy trigger wywołujący błąd przy kolejnym zapisie ceny potwierdza rollback po faktycznym błędzie bazy, nie tylko po walidacji.

Wykonano `dotnet build E-commerce-backend.slnx --configuration Release` i pełne `dotnet test E-commerce-backend.slnx --configuration Release` z lokalnym PostgreSQL: **116 testów zaliczonych, 0 pominiętych**. Tryb Release pozwala uniknąć nadpisywania plików używanych przez uruchomione developerskie API Debug. `dotnet ef migrations has-pending-model-changes --configuration Release --no-build` nie wskazuje różnic.

Migrację promocji sprawdzono także na 43 467 cenach (niezmieniona liczba i kontrolny skrót) oraz zastosowano w Supabase. Pomiar 5000 produktów jest w [audycie](catalog-performance-audit.md#promocje-produktu--31102026). Nie przeprowadzono pełnego testu promocji z dwoma rzeczywistymi kontami Supabase; lokalne testy kontroli dostępu używają atrapy istniejącego mechanizmu sesji.

## Odbiór promocji przez HTTPS — 01.11.2026

Wykonano pełny przebieg przez rzeczywisty pipeline ASP.NET Core, HTTPS i cookie `__Host-ecomm-auth`, bez atrapy Auth. Dwa nowe konta (administrator i klient) utworzono przez Supabase Admin API z potwierdzonym e-mailem, bez wysyłania wiadomości. Logowanie i potwierdzanie sesji korzystało z rzeczywistego Supabase Auth. Katalog, lokalna kopia ID użytkowników i uprawnienie administratora istniały wyłącznie w izolowanej bazie PostgreSQL `ecomm_test_promotion_http`. Nie zmieniano katalogu ani uprawnień sklepu w Supabase.

Wynik: **23/23 sprawdzenia HTTP zaliczone**. Powtórzony build: 0 błędów/ostrzeżeń; pełny zestaw xUnit na świeżo zmigrowanej lokalnej bazie: **116 zaliczonych, 0 pominiętych**. Sprawdzono:

- Powiązanie uruchomionego API z izolowaną bazą przez losowy znacznik kategorii przed wywołaniem resetującego seedera; HTTP seed ze scenariuszami promocji i licznikami.
- Logowanie obu rzeczywistych kont, cookie, `/auth/me`, `401` dla anonimowego i `403` dla klienta na wszystkich siedmiu trasach promocji. Klient miał celowo `role=admin` w `user_metadata`, co nie dało uprawnień.
- Brak nagłówka CSRF (`403`), niepoprawny UUID klucza i serwerowe pole `actorUserId` (`400` przez MVC).
- Szkic, ponowienie jego utworzenia, stronicowany podgląd z pełną listą wersji, aktywację, konflikt klucza z inną treścią i blokadę zwykłej zmiany ceny.
- Spójne kwoty i odniesienie w szczegółach, katalogu, kategorii i POST wyszukiwania z ceną.
- Ponowienie aktywacji i zakończenia bez nowych wierszy, przywrócenie podstawy i `reduction=null`, blokadę reaktywacji zakończonej akcji.
- Następną promocję kwotową: cenę po rabacie i odniesienie uwzględniające poprzednią obniżkę. Dwa pełne cykle pozostawiły dokładnie cztery nowe wpisy historii per wariant.
- Odebranie administratora z zachowaniem tej samej sesji, a następnie `403`.

Po każdym przebiegu wylogowano tymczasowe konta, usunięto je przez Admin API i zweryfikowano `404` przy ponownym odczycie. Test nie używa istniejących haseł użytkowników, nie wysyła e-maili i nie nadaje administratora w bazie sklepu. Testowy serwer API i lokalny PostgreSQL zatrzymano po pracy.

W pierwszym uruchomieniu helpera PowerShell nowy klucz `sb_secret` został odrzucony z `401`, ponieważ domyślny User-Agent wyglądał jak przeglądarka. Helper serwerowy jawnie deklaruje `ecomm-smoke-server/1.0`; kod API nie wymagał zmiany. To zachowanie opisuje [dokumentacja kluczy Supabase](https://supabase.com/docs/guides/getting-started/api-keys#secret-keys-and-elevated-access). W asercji drugiego rabatu poprawiono wybór wariantu po UUID: kolejność podglądu jest według UUID, a szczegółów według SKU. Nie wykryto błędu aplikacyjnej logiki promocji.

### Odtworzenie testu

Skrypt [tests/smoke/promotions.ps1](../tests/smoke/promotions.ps1) wymaga PowerShell 7, lokalnego `psql`, świeżej bazy `ecomm_test_*` z migracjami, developerskiego certyfikatu HTTPS oraz istniejącej konfiguracji Supabase w lokalnych user-secrets. Tworzy i usuwa tymczasowe konta w skonfigurowanym projekcie Supabase. Używa tajnego klucza tylko w pamięci po stronie narzędzia developerskiego, bez zapisywania go w raporcie.

Uruchom osobną instancję API ze zmiennymi `ASPNETCORE_ENVIRONMENT=Development`, `ConnectionStrings__SupabaseDatabase` wskazującym wyłącznie lokalną bazę testową, `DemoSeed__Enabled=true`, tymczasowym `DemoSeed__Key` i adresem `https://localhost:15476`. Przykładowo dla bazy PostgreSQL na porcie 15474 (wcześniej bootstrap i migracje według instrukcji na początku pliku):

```powershell
$qaSeedKey = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ConnectionStrings__SupabaseDatabase = 'Host=127.0.0.1;Port=15474;Database=ecomm_test_promotion_http;Username=postgres'
$env:DemoSeed__Enabled = 'true'
$env:DemoSeed__Key = $qaSeedKey
$env:ECOMMERCE_SMOKE_SEED_KEY = $qaSeedKey
```

Z tym samym środowiskiem uruchom `dotnet run --configuration Release --no-launch-profile --urls https://localhost:15476`, a w drugiej konsoli ustaw identyczny `ECOMMERCE_SMOKE_SEED_KEY` i wykonaj:

```powershell
./tests/smoke/promotions.ps1 -Api 'https://localhost:15476/api/v1/web' -Database ecomm_test_promotion_http -PgPort 15474
```

Skrypt nie przyjmuje zdalnego adresu API ani nazwy bazy spoza `ecomm_test_*`. Przed resetem sprawdza odczyt losowego lokalnego rekordu przez API. Pomija sprawdzanie łańcucha certyfikatu wyłącznie dla wskazanego lokalnego HTTPS; żądania do Supabase używają normalnej walidacji TLS. `finally` sprząta konta także po nieudanej asercji, a błąd sprzątania podaje UUID wymagające weryfikacji.

Granica dowodu: sprawdzono rzeczywisty Auth i transport HTTP wraz z lokalnym PostgreSQL, nie pełne połączenie katalogowe do zdalnej bazy Supabase, środowisko produkcyjne ani przeglądarkowy frontend. Nadal nie jest to test wszystkich ustawień CORS/SameSite przyszłego frontendu.

## Kolekcje — weryfikacja 01.11.2026

`CollectionEndpointTests` sprawdza dostęp do sześciu tras administracji, selektory, pola serwerowe, duplikaty ID i paginację przed IO. `CollectionPostgresTests` obejmuje metadane/skład/statusy, wersje generowane przez SQL, slug, publikację, odpowiedzi i ponowienia, publiczne odczyty i przecięcie z kategorią, zakres zamrożony po usunięciu produktu/ukryciu, równoczesne edycje i aktywacje, konflikty produktu/kolekcji i dwóch kolekcji, rollback po rzeczywistym błędzie ceny oraz dawny fingerprint i JSON operacji. Największy seeder zapisuje 500 produktów i 3000 wariantów; dodatkowy wariant pozwala sprawdzić limit 3001 przed kalkulacją historii. Odroczone ograniczenie publikacji i CHECK celu promocji są testowane również przez SQL.

Wynik pełnego zestawu: **132 zaliczone, 0 pominiętych** na lokalnym PostgreSQL 17, build Release z 0 błędów i ostrzeżeń. Migracja od zera, bezdanych Down/Up i aktualizacja większej bazy przeszły; 45 029 cen i 41 operacji zachowały identyczne skróty wszystkich pól. Migrację kolekcji zastosowano też w Supabase. Szczegóły pomiaru są w [audycie](catalog-performance-audit.md#kolekcje--pomiar-01112026).

Skrypt `tests/smoke/promotions.ps1` ma teraz **32 sprawdzenia HTTPS**. Oprócz dotychczasowej promocji produktu tworzy kolekcję, przypisuje produkty, publikuje, odczytuje paginację, odrzuca zwykłego klienta na sześciu trasach, wykrywa zmianę po podglądzie, aktywuje rabat, sprawdza zakres kolekcja/kategoria/cena, usuwa produkt z kolekcji i ukrywa ją, ponawia aktywację oraz kończy promocję z utrwalonych udziałów. Test korzysta z rzeczywistego Supabase Auth, dwóch tymczasowych kont i wyłącznie lokalnego katalogu; oba konta wylogowano, usunięto i potwierdzono `404`. Sposób uruchomienia pozostaje opisany powyżej.

Seeder losuje liczbę wariantów. Rabat kwotowy w smoke jest teraz dobierany względem najdroższego wariantu, aby był niższy od poprzedniej ceny każdego wariantu; stała wyliczona tylko z najtańszego mogła słusznie blokować cały podgląd. Nie zmieniano z tego powodu reguł aplikacji.
