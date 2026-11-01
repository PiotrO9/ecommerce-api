# Pomiar katalogu na 5000 produktów

Cel: odtworzyć lokalny pomiar odczytu katalogu, wyszukiwania, cen i liczników filtrów. Skrypty są przeznaczone wyłącznie do odizolowanej bazy PostgreSQL. Zbiór jest syntetyczny, więc wyniki nie są prognozą czasu odpowiedzi Supabase.

## Przygotowanie danych

1. Utwórz osobną bazę o nazwie zaczynającej się od `ecomm_perf`. Na zwykłym PostgreSQL, który nie ma schematu Supabase Auth i jego ról, uruchom `tests/performance/bootstrap-local-postgres.sql` na tej bazie przed migracjami EF Core. Skrypt tworzy tylko minimalne obiekty potrzebne migracjom. Nie używaj bazy projektu Supabase.

```powershell
psql -h 127.0.0.1 -U postgres -d ecomm_perf_catalog -f tests/performance/bootstrap-local-postgres.sql
$env:ConnectionStrings__SupabaseDatabase = 'Host=127.0.0.1;Database=ecomm_perf_catalog;Username=postgres;Password=...'
dotnet ef database update
```

2. Uruchom developerski seeder na tej bazie z body `{"categories":"high","products":"high","variants":"high"}`. Seeder resetuje katalog i odświeża statystyki PostgreSQL przez `ANALYZE`.
3. Dopełnij katalog do dokładnie 5000 produktów (3 warianty, 5 cech i 3 ceny historyczne na dodatkowy produkt):

```powershell
psql -h 127.0.0.1 -U postgres -d ecomm_perf_catalog -f tests/performance/large-catalog.sql
```

Skrypt SQL odmawia pracy poza bazą `ecomm_perf*` i przy ponownym uruchomieniu. Jego dodatkowe wiersze są zapisywane w jednej transakcji, po czym aktualizowane są statystyki. Skrypt czasowo wyłącza wskazane wyzwalacze użytkownika podczas masowego ładowania; należy go stosować wyłącznie na danych odtwarzalnych.

## Pomiary HTTP

Uruchom API z połączeniem do tej samej bazy, a następnie:

```powershell
./tests/performance/benchmark.ps1 -Phase controlled `
  -Base 'http://127.0.0.1:5294/api/v1/web/products' `
  -Output './controlled-results.json' -Samples 40

dotnet run --project tests/performance/load.csproj -- `
  http://127.0.0.1:5294/api/v1/web/products
```

`benchmark.ps1` wykonuje 5 rozgrzewek oraz 40 kolejnych mierzonych żądań na scenariusz i zapisuje p50/p95 do JSON. Dostępne są `-Phase large`, `-Phase controlled`, `-Phase facets` (trzy przypadki bez ceny) i `-Phase price` (cenowy GET). `load.csproj` wykonuje 3 rozgrzewki i po 50 żądań na scenariusz z maksymalnie pięcioma jednoczesnymi. Porównuj wyniki dla tej samej bazy, ustawień API, liczby próbek i bez diagnostycznego logowania SQL. Szczegóły zestawu bazowego i poprzednie wyniki są w [audycie](../../docs/catalog-performance-audit.md).

## Pomiar promocji

Na przygotowanej i zmigrowanej lokalnej bazie `ecomm_perf*` z 5000 syntetycznych produktów:

```powershell
$env:ECOMMERCE_PERFORMANCE_DATABASE = 'Host=127.0.0.1;Database=ecomm_perf_catalog;Username=postgres;Password=...'
dotnet run --configuration Release --project tests/performance/promotions/promotions.csproj
dotnet run --configuration Release --project tests/performance/promotions/promotions.csproj -- --activate-existing
dotnet run --configuration Release --project tests/performance/promotions/promotions.csproj -- --read-count
```

Pierwsze polecenie dodaje 50 unikalnych wariantów do `demo-czapka`, syntetyczną historię oraz wykonuje 3 rozgrzewki i 10 mierzonych cykli. Wypisuje p50/p95 i liczbę komend EF dla podglądu/startu/końca. Odmawia ponownego przygotowania tej samej bazy; odtwórz bazę demonstracyjną do kolejnej serii. `--activate-existing` uruchamia większy rabat po serii, do pomiaru odczytów HTTP podczas promocji. `--read-count` sprawdza liczbę komend katalogu i wyszukiwania. Żaden z tych trybów nie przyjmuje połączenia zdalnego ani nazwy bazy spoza `ecomm_perf*`.

Harness zmienia dane syntetyczne. Nie kieruj go na projekt Supabase. Jeżeli API Release korzysta z tych samych plików wynikowych, zatrzymaj własną instancję pomiarową przed przebudową albo użyj osobnego katalogu artifacts.

## Pomiar kolekcji

Na odizolowanej lokalnej bazie `ecomm_perf*` z przygotowanymi 5000 produktami i migracją kolekcji:

```powershell
$env:ECOMMERCE_PERFORMANCE_DATABASE = 'Host=127.0.0.1;Port=15474;Database=ecomm_perf;Username=postgres'
dotnet run --configuration Release --project tests/performance/collections/collections.csproj
```

Harness wybiera pierwszych 500 syntetycznych produktów `perf-produkt-*`, dodaje jawnie demonstracyjną historię oferty i regularne ceny, tworzy kolekcje 10/100/500 produktów i mierzy 3 rozgrzewki + 5 cykli podglądu/startu/końca. Pokazuje liczbę komend oraz pojedyncze oczekiwanie równoległej transakcji na blokadę zapisu. Odmawia pracy zdalnej, bazy spoza `ecomm_perf*` i ponownego przygotowania tej samej bazy.

Zmienia wyłącznie dane syntetyczne: do nowej serii odtwórz bazę testową. Wyniki i ograniczenia próby są w [audycie](../../docs/catalog-performance-audit.md#kolekcje--pomiar-01112026). Do porównania HTTP przed/po użyj `benchmark.ps1 -Phase controlled` na tej samej bazie, **przed** uruchomieniem mutującego harnessu.
