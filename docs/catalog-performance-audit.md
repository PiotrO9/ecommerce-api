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
## Promocje produktu — 31.10.2026

Na dotychczasowej izolowanej lokalnej bazie 5000 produktów zastosowano nową migrację. Przed i po: 43 467 cen, identyczne MD5 ID/kwot/dat. Późniejszy harness dopisał 50 wariantów do istniejącej `demo-czapka`, bez nowych produktów; akcja objęła **56 wariantów**. Historia oferty do pomiaru była jawnie syntetyczna (`source=demo`). Nie wykonywano resetu ani testowych zmian cen w Supabase.

Pomiar serwisu przez EF Core na PostgreSQL 17, 3 pełne cykle rozgrzewające i 10 mierzonych. Każdy kolejny rabat był większy, aby respektować minimum po poprzedniej obniżce. Czasy obejmują transakcję i zapis w bazie, bez HTTP i Supabase Auth. Licznik obejmuje komendy aplikacji; nie liczy wewnętrznych poleceń wykonywanych przez triggery i samego BEGIN/COMMIT.

| Operacja, 56 wariantów | p50 | p95 | Komendy SQL |
| --- | ---: | ---: | ---: |
| Podgląd (strona 20, kontrola pełnego zakresu) | 6,9 ms | 8,5 ms | 7 |
| Aktywacja | 36,0 ms | 49,1 ms | 12 |
| Zakończenie | 21,3 ms | 28,9 ms | 10 |

Po pomiarze uruchomiono aktywną promocję tego produktu i lokalne HTTP API bez logowania SQL. GET z ceną 100–300 PLN i `price-asc`, 5 rozgrzewek i 40 mierzonych żądań: **p50 77,2 ms, p95 79,6 ms**. Klient obciążeniowy wykonał po 3 rozgrzewki i 50 żądań na scenariusz, maksymalnie 5 równocześnie:

| Scenariusz HTTP | p50 | p95 | Błędy / żądania |
| --- | ---: | ---: | ---: |
| Strona katalogu | 41,6 ms | 117,7 ms | 0 / 50 |
| Puste wyszukiwanie | 137,0 ms | 164,3 ms | 0 / 50 |
| Sortowanie i filtr ceny | 80,9 ms | 92,1 ms | 0 / 50 |
| Dwie cechy + cena + sortowanie | 216,9 ms | 243,0 ms | 0 / 50 |
| Pięć cech | 152,2 ms | 169,3 ms | 0 / 50 |

Diagnostyka przez interceptor EF potwierdziła 4 komendy dla katalogu z ceną i 6 dla wyszukiwania z dwiema cechami/ceną; dodanie odniesienia nie tworzy zapytania per wariant. Cała historia pobierana do kalkulatora obejmuje wyłącznie warianty jednego produktu. Aktywacja i zakończenie nie wykonują osobnego zapytania historii/odniesienia dla każdego wariantu.

Wyniki są wystarczające do dalszego lokalnego rozwoju tej wersji. To nie prognoza Supabase ani test wielu jednoczesnych administratorów; 10 cykli zapisu to mała próba, p95 odpowiada największej zmierzonej wartości. Wspólna blokada serializuje zapisy katalogu, więc większy ruch administracyjny wymaga pomiaru oczekiwania na blokadę. Nie wykonano nowego porównania A–B–A, więc tej serii nie należy traktować jako dowodu poprawy względem poprzednich pomiarów.

Odtworzenie: [harness promocji](../tests/performance/README.md#pomiar-promocji).

## Kolekcje — pomiar 01.11.2026

Na tej samej lokalnej bazie 5000 produktów porównano przed zmianą danych build z commita `8c9ae7f` i nowy build kolekcji, oba z najnowszym schematem. Serwery działały kolejno, bez logowania SQL. Każdy scenariusz: 5 rozgrzewek, 20 pomiarów HTTP, jeden klient. Wszystkie odpowiedzi `200`, identyczne liczby wyników:

| Wyszukiwanie | Przed p50 / p95 | Po p50 / p95 |
| --- | ---: | ---: |
| Jedna cecha | 127,6 / 197,3 ms | 132,8 / 233,6 ms |
| Dwie cechy | 201,4 / 214,9 ms | 200,5 / 205,9 ms |
| Pięć cech | 216,1 / 221,7 ms | 215,2 / 221,5 ms |
| Tylko cena | 269,3 / 280,2 ms | 256,6 / 276,1 ms |
| Dwie cechy i cena | 277,9 / 287,6 ms | 276,7 / 282,1 ms |
| Dwie cechy, cena i sort | 341,8 / 357,9 ms | 342,0 / 368,0 ms |

Mediany są zbliżone; wzrosło p95 pierwszego scenariusza. To pojedyncze porównanie A–B, więc nie rozstrzyga, czy ogon pierwszego przypadku to wahanie środowiska. Nie ma dowodu poprawy wydajności. Nowe zawężenie kolekcji nie jest wykonywane bez jej selektora. W razie powtarzalnego pogorszenia trzeba odtworzyć A–B–A i zebrać plany; wyniki tej sesji nie są bezpośrednio porównywalne z innym dniem audytu.

Następnie harness `tests/performance/collections/` przygotował jawnie syntetyczną kompletną ofertę wybranych wariantów (`source=demo`) i regularne ceny startowe w izolowanej bazie. Istniejący zbiór performance miał niezarządzane rabaty, których normalny serwis słusznie nie nadpisuje. Nie wykonano takiego przygotowania w Supabase. Dla każdego rozmiaru wykonano 3 cykle rozgrzewające i 5 mierzonych; każdy kolejny rabat był większy i uwzględniał minimum wcześniejszej akcji. Czasy serwisu obejmują EF/transakcję, bez HTTP i zewnętrznego Auth.

| Produkty / warianty | Podgląd p50 / p95 | Aktywacja p50 / p95 | Koniec p50 / p95 |
| --- | ---: | ---: | ---: |
| 10 / 30 | 5,1 / 5,8 ms | 18,4 / 20,4 ms | 10,5 / 18,7 ms |
| 100 / 300 | 52,8 / 63,0 ms | 308,8 / 423,8 ms | 198,0 / 245,1 ms |
| 500 / 1500 | 87,0 / 124,5 ms | 748,0 / 779,0 ms | 372,2 / 412,1 ms |

Komendy EF: podgląd 10 dla każdego rozmiaru; start 15/15/19, koniec 10/10/11 (więcej batchy zapisu przy dużym zestawie), wyszukiwanie w kolekcji 7. Brak osobnego zapytania dla każdego produktu/wariantu. Licznik nie obejmuje wewnętrznej pracy triggerów ani BEGIN/COMMIT.

Pojedyncza równoległa transakcja czekająca na tę samą blokadę podczas ostatniej aktywacji czekała odpowiednio 0,5 / 335,6 / 694,1 ms. To obserwacja mechanizmu serializacji, nie p95 ruchu wielu administratorów. Najmniejsza aktywacja mogła niemal skończyć się przed nadejściem drugiego żądania. Przy pięciu próbkach p95 jest maksimum; pomiar nie uzasadnia prognozy dla zdalnego Supabase.

Wniosek projektowy: synchroniczna akcja 1500 wariantów jest użyteczna na tym lokalnym zbiorze, ale blokuje innych piszących przez znaczną część sekundy. Zachowano roboczy limit 3000 wariantów kolekcji; nie zmierzono czasu na jego granicy. Test PostgreSQL potwierdza odrzucenie 3001 wariantów przed kalkulacją historii i poprawny zapis maksymalnego seedera 500 produktów / 3000 wariantów. Dalsze zwiększanie limitu wymaga pomiaru, zwłaszcza na bazie z długą historią.
