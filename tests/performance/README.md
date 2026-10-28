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
