# Audyt wydajności odczytu katalogu

Cel: zachować warunki, wyniki przed i po optymalizacji wyszukiwania, liczników filtrów i cen z 28.10.2026. To wynik testu na danych syntetycznych, nie gwarancja czasu odpowiedzi w środowisku produkcyjnym. Pomiary `q` we wcześniejszych sekcjach wykonano przed dodaniem domyślnego rankingu trafności; jego koszt zmierzono osobno na końcu dokumentu.

## Stan kodu przed optymalizacją

- Stronicowany katalog wykonuje cztery polecenia SQL: licznik produktów, stronę produktów, pasujące warianty oraz bieżące ceny. `POST /api/v1/web/products/search` dodaje zapytanie o klucze cech, zapytanie o podstawowe liczniki oraz po jednym zapytaniu licznikowym dla każdego wybranego klucza. Dwa wybrane klucze oznaczają łącznie osiem poleceń SQL. Odpowiada za to `ProductsController.BuildAvailableFiltersAsync`.
- Wyszukiwanie nazwy i SKU stosuje `ILIKE` z wzorcem `%fraza%`. Sortowanie po cenie wyznacza najniższą bieżącą cenę PLN z pasujących wariantów przed stronicowaniem. Zasady kontraktu pozostają opisane w [api-contract.md](api-contract.md).

## Środowisko i dane pomiarowe

- API ASP.NET Core/.NET 10 i PostgreSQL 17 działały na tym samym komputerze przez `127.0.0.1`; Docker Engine był niedostępny. PostgreSQL uruchomiono w osobnym tymczasowym klastrze, z migracjami projektu. Połączenie do Supabase nie było używane, a jego katalog nie był zmieniany.
- Pierwszy zestaw: seeder `high/high/high` utworzył 436 produktów, 2390 wariantów, 2391 rekordów cen i 51 kategorii.
- Większy zestaw: dodano syntetyczne produkty z trzema wariantami, pięcioma cechami na wariant i trzema zmianami ceny na wariant. Końcowo baza miała **5000 produktów, 16 082 warianty, 43 467 rekordów cen, 73 238 powiązań cech i 51 kategorii**. Każdy produkt miał wariant, każdy wariant cenę, a zestawy cech wariantów w obrębie produktu były unikalne. Baza zajmowała około 35,4 MiB.
- Po załadowaniu obu zestawów wykonano `ANALYZE` na tabelach katalogu. Dla każdego scenariusza wykonano 5 żądań rozgrzewających i 40 mierzonych, kolejno, jednym klientem HTTP; mierzono pełną odpowiedź. `pageSize=20`. Podano medianę (`p50`) i 95. percentyl (`p95`), w milisekundach.

## Wyniki pojedynczego klienta

| Scenariusz | 436 produktów p50 / p95 | 5000 produktów p50 / p95 |
| --- | ---: | ---: |
| `GET /catalog`, strona 1 | 16,4 / 18,6 ms | 75,4 / 79,1 ms |
| `GET /catalog?q=demo`, strona 1 | 17,3 / 22,1 ms | 96,4 / 100,6 ms |
| `GET /catalog?minPrice=100&maxPrice=300&sort=price-asc`, strona 1 | 48,5 / 55,6 ms | 448,4 / 460,7 ms |
| `POST /search` z `{}` | 31,7 / 34,5 ms | 245,6 / 263,1 ms |
| `POST /search` z dwiema cechami demonstracyjnymi | 61,6 / 65,6 ms | 238,7 / 250,6 ms |

W kontrolowanej serii na 5000 produktów (po 40 żądań) uzyskano:

| `POST /search` | Pasujące produkty | p50 | p95 |
| --- | ---: | ---: | ---: |
| Jedna wybrana cecha syntetyczna, bez ceny | 4564 | 147,1 ms | 152,5 ms |
| Dwie cechy, bez ceny | 4564 | 173,4 ms | 178,3 ms |
| Pięć cech, bez ceny | 1521 | 276,9 ms | 326,7 ms |
| Tylko cena 100–300 PLN | 1993 | 297,2 ms | 338,4 ms |
| Dwie cechy i cena 100–300 PLN | 1810 | 591,0 ms | 612,2 ms |
| Dwie cechy, cena 100–300 PLN i `price-asc` | 1810 | 739,8 ms | 755,9 ms |

Osobna wcześniejsza seria tych samych dwóch cech z ceną dała p50 633,4 ms i p95 725,8 ms. Różnica między seriami pokazuje zmienność pomiaru; nie należy traktować pojedynczej wartości jako stałej.

## Pięć równoległych żądań

Po 3 żądaniach rozgrzewających wykonano po 50 żądań na scenariusz, z maksymalnie pięcioma jednocześnie. Wszystkie zwróciły `200`.

| Scenariusz | p50 | p95 | Przepustowość w tej próbie |
| --- | ---: | ---: | ---: |
| Strona katalogu | 51,9 ms | 159,0 ms | 78,3 żądania/s |
| `POST /search` z `{}` | 150,3 ms | 193,9 ms | 31,9 żądania/s |
| Sortowanie i zakres ceny w katalogu | 263,0 ms | 288,4 ms | 18,5 żądania/s |
| Dwie cechy, cena i `price-asc` w `POST /search` | 797,9 ms | **1835,4 ms** | 4,7 żądania/s |
| Pięć cech bez ceny w `POST /search` | 294,2 ms | 311,8 ms | 16,7 żądania/s |

## Rozpoznane przyczyny i granice wniosków

- Bez aktualnych statystyk, tuż po pierwszym seedzie, `POST /search` z dwiema cechami zwrócił `500` po 30 sekundach. To samo polecenie `COUNT` uruchomione bezpośrednio w PostgreSQL nie skończyło się w 45 sekund. Plan błędnie oszacował liczbę wierszy i wybrał kosztowne zagnieżdżone połączenia. Po `ANALYZE` wybrał połączenia typu hash; `EXPLAIN (ANALYZE, BUFFERS)` pokazał 13,6 ms planowania i 2,5 ms wykonania tego `COUNT`. PostgreSQL dokumentuje wpływ aktualnych statystyk na wybór planu: [ANALYZE](https://www.postgresql.org/docs/current/sql-analyze.html), [EXPLAIN](https://www.postgresql.org/docs/current/using-explain.html).
- Po rozgrzaniu, dla żądania z dwiema cechami, ceną i `price-asc`, osiem poleceń SQL zajęło w jednej próbie odpowiednio około **79, 250, 4, 0, 64, 113, 124 i 122 ms**. Najdroższe były pobranie strony posortowanej po cenie oraz trzy zapytania licznikowe filtrów. Pomiar z włączonym logowaniem diagnostycznym służy do wskazania kosztownych części, nie do porównania bezpośrednio z tabelą czasów bez logowania.
- Zwykła strona katalogu działa sprawnie na lokalnym zbiorze 5000 produktów. Interaktywne wyszukiwanie z ceną i cechami jest wyraźnie wolniejsze; p95 około 756 ms przy jednym kliencie i 1,84 s przy pięciu równoległych żądaniach jest **nieadekwatne jako docelowy czas reakcji interaktywnego filtrowania**. To ocena projektowa, a nie przyjęte SLA.
- Wyniki dotyczą lokalnego API i lokalnej bazy, ciepłego cache oraz powtarzanych żądań. Produkcyjne dane, sieć do Supabase, rozkład fraz i ruch wielu użytkowników mogą zmienić czasy. Dane syntetyczne były rozłożone regularnie; potrzebne są później pomiary w środowisku zbliżonym do docelowego.

## Wdrożenie

- Dodano test integracyjny PostgreSQL dla dopasowania cech i ceny na tym samym wariancie, liczników produktów i alternatywnych wartości, historii ceny oraz sortowania z wariantem bez PLN.
- Seeder po `TRUNCATE` i zapisie uruchamia `ANALYZE` dziewięciu tabel. Wcześniejsza próba bez statystyk wykazała zapytanie przekraczające 45 sekund; po analizie to samo `COUNT` wykonało się w 2,5 ms według `EXPLAIN`.
- Bieżąca cena PLN jest wyznaczana przez `DISTINCT ON (variant_id)` z kolejnością `effective_at DESC, created_at DESC, id DESC`. Strona sortowana po cenie wyznacza jedną minimalną cenę na produkt z pasujących wariantów i używa tego wyniku również do wykluczenia produktów bez pasujących wariantów. Brak ceny PLN nadal sortuje się na końcu.
- Gdy żądanie nie wskazuje kategorii, zapytanie o warianty nie dokłada zbędnego sprawdzenia przynależności do nieograniczonego zakresu produktów.
- Kontekstowe liczniki filtrów są liczone jednym zapytaniem z pominięciem własnego wybranego klucza, z zachowaniem warunku zgodności pozostałych cech w tym samym wariancie. Razem z zapytaniem o nazwy kluczy `POST /search` wykonuje teraz sześć poleceń SQL zamiast ośmiu przy dwóch wybranych kluczach.
- `EXPLAIN (ANALYZE, BUFFERS)` dla bieżących cen na 43 467 rekordach: przed indeksem sekwencyjny skan i sortowanie trwały 16,7 ms, po częściowym indeksie dla PLN pojawił się `Index Only Scan` i czas 7,7 ms. Migracja `20261028110446_AddCurrentPlnPriceIndex` tworzy ten indeks. Zastosowano ją na odizolowanej bazie testowej i w skonfigurowanej bazie Supabase; lista migracji EF potwierdziła brak oczekujących migracji.
- Powtarzalny generator większego katalogu i klient pomiarowy są w [`tests/performance/`](../tests/performance/README.md). Generator został wykonany na drugiej izolowanej bazie: 401 produktów seedera dopełnił do 5000 produktów, 15 996 wariantów i 43 591 rekordów cen.

## Ponowny pomiar na tej samej bazie 5000 produktów

API i PostgreSQL działały lokalnie, bez diagnostycznego logowania SQL. Zachowano tę samą bazę i te same scenariusze co przed zmianą. W tabeli pokazano p95 z 40 żądań po pięciu rozgrzewkach.

| Scenariusz | Przed | Po |
| --- | ---: | ---: |
| GET sortowanie i zakres ceny PLN | 460,7 ms | **129,4 ms** |
| POST dwie cechy, cena i `price-asc` | 755,9 ms | **408,8 ms** |
| POST dwie cechy i cena, bez sortowania | 612,2 ms | **383,5 ms** |
| POST pięć cech, bez ceny | 326,7 ms | 317,0 ms |
| POST jedna cecha, bez ceny | 152,5 ms | **219,4 ms** |
| POST dwie cechy, bez ceny | 178,3 ms | **223,3 ms** |

Dla 50 żądań na scenariusz przy maksymalnie pięciu jednoczesnych, z trzema rozgrzewkami, złożony POST z dwiema cechami, ceną i `price-asc` osiągnął p95 **424,6 ms** i 12,0 żądań/s; wcześniej było 1835,4 ms i 4,7 żądania/s. Wszystkie żądania zwróciły `200`. GET sortowany po cenie miał p95 129,8 ms przy pięciu jednoczesnych.

Robocze progi p95 dla lokalnego zestawu, 300 ms dla cenowego GET i 500 ms dla złożonego POST z pięcioma klientami, zostały osiągnięte, ale drugi z nich ma niewielki zapas. W tej serii proste wyszukiwanie z jedną lub dwiema cechami bez ceny było wolniejsze; wynik ten sprawdzono ponownie poniżej. Wyniki mogą się zmienić przy innym rozkładzie danych, zimnym cache, ruchu produkcyjnym i opóźnieniu sieci do Supabase.

## Kontrola podejrzenia regresji prostych filtrów

Ponowiono pomiar na tej samej bazie 5000 produktów, z wyłączonym logowaniem SQL, po pięciu rozgrzewkach i z 40 żądaniami na scenariusz. Dla porównania uruchomiono też poprzedni commit `1d91bba` na tej samej bazie i tym samym komputerze; następnie ponownie zmierzono aktualny commit `1479618`. Wersje działały kolejno, bez równoczesnych żądań.

| Wersja i seria | Jedna cecha p95 | Dwie cechy p95 | Pięć cech p95 |
| --- | ---: | ---: | ---: |
| Aktualna, seria 1 | 135,2 ms | 134,9 ms | 148,9 ms |
| Aktualna, seria 2 | 181,9 ms | 126,9 ms | 139,4 ms |
| Poprzednia | 159,8 ms | 181,1 ms | 284,4 ms |
| Aktualna, seria 3 | 137,1 ms | 135,0 ms | 145,8 ms |

W logu diagnostycznym dla dwóch cech aktualna wersja wykonała sześć zapytań SQL, w tym jedno liczące wartości filtrów (około 50 ms w ciepłej próbie). Poprzednia wykonała osiem zapytań, w tym trzy liczące wartości filtrów (około 40, 24 i 23 ms). `EXPLAIN (ANALYZE, BUFFERS)` na tych samych danych pokazał dla aktualnego zapytania licznikowego 8,7 ms planowania i 61,7 ms wykonania; trzy stare zapytania wymagały łącznie 29,5 ms planowania i 108,2 ms wykonania. Czasy `EXPLAIN` nie są bezpośrednim pomiarem HTTP, ale potwierdzają koszt dodatkowych poleceń.

**Wniosek:** wcześniejsze spowolnienie 219–223 ms nie powtórzyło się stabilnie. W porównaniu A–B–A aktualny kod był zwykle szybszy, zwłaszcza dla dwóch i pięciu cech. Przyczyna wahania pierwszej serii nie została ustalona, dlatego nie ma podstaw do zmiany zapytania ani dodania indeksu tylko na podstawie tamtego wyniku. Jeśli problem pojawi się w środowisku docelowym, należy zebrać pomiary z tego środowiska i ponownie sprawdzić plany.

## Krótki pomiar rankingu trafności

Na tej samej lokalnej bazie 5000 produktów uruchomiono GET katalogu z frazą perf, stroną 1 i rozmiarem strony 20. API działało bez logowania poleceń SQL. W kolejnych seriach A–B–A wykonano po 5 żądań rozgrzewających i 30 mierzonych, jednym klientem. Każdy wariant zwrócił 4564 pasujące produkty.

| Sortowanie | p50 | p95 |
| --- | ---: | ---: |
| Domyślne po trafności, seria 1 | 115,5 ms | 125,6 ms |
| Jawne name-asc | 53,3 ms | 66,2 ms |
| Domyślne po trafności, seria 2 | 114,6 ms | 116,5 ms |

Ranking zwiększył czas tego szerokiego zapytania około dwukrotnie, ale p95 pozostało blisko 120 ms w tej lokalnej próbie. To pomiar na ciepłej bazie, bez ruchu równoległego. Przy większym katalogu lub innym rozkładzie zapytań należy zmierzyć ponownie przed doborem indeksów.