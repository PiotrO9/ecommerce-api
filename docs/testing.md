# Testy backendu

Cel: opisać układ testów i sposób ich uruchamiania w tym repozytorium.

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
