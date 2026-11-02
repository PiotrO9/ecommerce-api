# Plan wdrożenia kolekcji i promocji kolekcji

Cel: dodać ręczne grupowanie produktów, publiczny odczyt kolekcji i promocje obejmujące ich produkty, wykorzystując obecny katalog i mechanizm cen.

Status na 01.11.2026: **wdrożone w kodzie** na polecenie użytkownika „Zrealizuj ten plan”. Reguły planu przyjęto jako zakres pierwszego wydania. Modele, endpointy, seeder i testy opisane poniżej istnieją w repozytorium; szczegóły odbioru i ograniczenia są na końcu dokumentu. Aktualny stan promocji opisuje [promotion-implementation-plan.md](promotion-implementation-plan.md), a kolejność modułów [project-direction.md](project-direction.md).

## 1. Punkt wyjścia i zakres wydania

Stan wyjściowy przed wdrożeniem: `Promotion` miał wymagane `ProductId`; `PromotionService` dobiera warianty jednego produktu. Istnieją podgląd, atomowy start/koniec, wersje, klucze ponowienia, historia cen i zapisane odniesienie wcześniejszej ceny. `promotion_variants` utrwala objęte warianty. Unikalny aktywny udział wariantu blokuje łączenie promocji. Odczyt produktów ma wspólne dopasowanie cech/ceny i kontekstowe liczniki filtrów. W tym stanie wyjściowym nie było encji ani endpointów kolekcji.

Wdrożony zakres: kolekcja jest ręcznie utrzymywaną grupą produktów, niezależną od kategorii, bez drzewa. Produkt może należeć do wielu kolekcji. Pierwsza wersja obejmuje model, minimalne zarządzanie kolekcjami, publiczny odczyt i promocję jednej kolekcji. Promocja całego katalogu, automatyczne reguły członkostwa, harmonogramy, bannery/zdjęcia i łączenie rabatów pozostają osobnymi zadaniami. Nie rozbudowujemy przy tym całego CRUD produktów i kategorii.

### Przyjęte reguły

- Kolekcja ma `Draft`, `Published`, `Hidden`. Szkic może być pusty; publikacja wymaga przynajmniej jednego aktualnie publicznego produktu. Późniejsze ukrycie produktów może dać pustą opublikowaną kolekcję — odczyt wtedy zwraca pustą listę, nie zmienia automatycznie statusu.
- Przypisywać można również istniejące produkty `Draft`/`Hidden`, aby przygotować kolekcję. Odczyt publiczny pokazuje tylko ofertę zgodną z dotychczasowymi regułami katalogu. Administrator widzi również ukryte przypisania.
- Ukrycie kolekcji nie ukrywa produktów w innych miejscach sklepu i nie kończy ich aktywnej promocji. Zmiana kolekcji nie jest zmianą publicznego oferowania wariantu: nie tworzy nowego `variant_offer_events`.
- Przy starcie promocji kolekcja musi być `Published`. Zakres obejmuje produkty `Published` z kategorią i ich warianty `Published`. Produkty/konfiguracje niewidoczne są jawnie wykazywane jako wykluczone w podglądzie. Nie pomijamy po cichu objętego wariantu bez poprawnej ceny PLN lub historii — taki błąd blokuje całą aktywację. Pusty zakres też blokuje start.
- Przynależność jest ustalana podczas aktywacji. Dodanie/usunięcie produktu z kolekcji później nie dopisuje/nie usuwa rabatu z już objętych wariantów. Zakończenie zawsze korzysta z utrwalonych udziałów, również po ukryciu kolekcji, produktu lub usunięciu przypisania.
- Zachowujemy jedną aktywną promocję per wariant. Konflikt promocji produktu i kolekcji albo dwóch kolekcji daje błąd całej aktywacji, bez automatycznego wybierania większego rabatu.
- PLN, kalkulatory kwot i wcześniejszej ceny, zaokrąglenia, blokada zwykłej zmiany ceny oraz niezmienność historii pozostają zgodne z wdrożonym modułem promocji.
- W pierwszej wersji nie dodajemy fizycznego usuwania kolekcji. Do wycofania służy `Hidden`; historia promocji i operacji zachowuje FK do kolekcji. Zmianę slugu dopuszczamy po walidacji, bez mechanizmu przekierowań w tym etapie.

Przykład zakresu: promocja obejmuje warianty produktów A i B. Administrator usuwa B z kolekcji i dodaje C. Lista kolekcji pokazuje A i C; rabat uruchomionej akcji nadal dotyczy A i B. Zakończenie przywraca podstawowe ceny A i B, bez zmian ceny C.

## 2. Model i migracja

Wdrożone dane w schemacie `app`:

| Obiekt | Pola / ograniczenia |
| --- | --- |
| `Collection` / `collections` | `Id` UUID, `DisplayName` (trim, 1–120), `Slug` (1–160, format jak slug produktu, globalnie unikalny), `Status`, `Version` dodatnia, `CreatedAt`, `UpdatedAt` UTC |
| `CollectionProduct` / `collection_products` | `CollectionId`, `ProductId` jako złożony PK i FK, `CreatedAt` UTC; bez powtórzeń przypisania; indeks po `ProductId` do odczytu odwrotnego |
| `CollectionOperation` / `collection_operations` | UUID, FK kolekcji, autor/klucz UUID, rodzaj operacji, hash żądania, oryginalny wynik JSON i kod odpowiedzi, data UTC; unikalny autor/klucz i niezmienny zapis |
| Rozszerzony `Promotion` | `Scope`: `product` albo `collection`; nullable `ProductId`, nullable `CollectionId`; CHECK wymaga dokładnie jednego odpowiedniego FK |

Opis i grafika kolekcji nie są wymagane do grupowania/promocji; można je dodać przy projektowaniu widoku. Nazwa kolekcji nie zmienia automatycznie zapisanego slugu. Pierwsza wersja wymaga jawnego slugu w żądaniu, żeby nie uzależniać tego etapu od niewdrożonego jeszcze generatora kategorii.

`CollectionOperation` jest osobnym dziennikiem, ponieważ obecny `PromotionOperation` wymaga FK do promocji. Dzięki temu ponowienie utworzenia/zmiany kolekcji nie tworzy drugiej kolekcji ani kolejnej wersji. Jego przestrzeń autor/klucz jest oddzielna od promocji i zwykłej ceny. Nie tworzymy wspólnego, przebudowywanego przy tej okazji systemu komend dla całej aplikacji.

Migracja nadaje istniejącym promocjom `Scope=product`, zachować ich `ProductId`, wersje, udziały, operacje i kwoty historii. Nowe FK mają `RESTRICT`; nowe tabele dostają RLS i odebranie dostępu rolom Data API według wzorca repozytorium. Nie zmieniamy Supabase Auth ani nie modyfikujemy niezmiennych starych operacji. Sprawdzić instalację od zera i migrację na danych z aktywną/zakończoną promocją produktu. Wycofanie po utworzeniu historii kolekcji wymaga świadomej migracji naprawczej.

## 3. Serwis i minimalna administracja kolekcji

Dodać `Services/Catalog/CollectionService`, encje/mapowania i DTO kolekcji. Metadane oraz cały skład zmieniamy z `expectedVersion`. Wersja rośnie także po zmianie statusu lub przypisań, aby podgląd promocji wykrywał zmianę kolekcji.

Operacje zapisu pobierają istniejący `CatalogWriteLock` przed innymi blokadami; dołączyć kolekcje i przypisania do wzorca triggera `BEFORE STATEMENT`. Zmiany przypisań przez SQL również muszą unieważniać wersję kolekcji. Źródłem inkrementacji są triggery metadanych i zmian przypisań; serwis nie podbija wersji drugi raz. Bezpośredni SQL nie może dowolnie ustawić nowej wartości wersji.

Po wszystkich zmianach przypisań serwis odczytuje wynikową wersję z bazy przed zapisaniem odpowiedzi operacji. Wersja musi być monotoniczna; nie zakładamy wzrostu dokładnie o 1 przy komendzie zmieniającej wiele relacji. Samo ponowienie zapisanej komendy nie wykonuje ponownie żadnej inkrementacji.

Zastąpienie listy produktów jest atomowe: walidacja duplikatów/istnienia wszystkich ID, wyznaczenie różnicy, usunięcie zbędnych i dodanie brakujących powiązań. Nie kasować i nie odtwarzać niezmienionych relacji. Zmiana pustą listą jest dozwolona w szkicu/ukrytej kolekcji; przy publikacji lub zapisie opublikowanej kolekcji sprawdzić regułę publicznej zawartości. Ponowienie komendy odtwarza oryginalny wynik, a inna treść pod tym samym kluczem daje `409`.

### Wdrożone endpointy administratora

Wspólny prefiks `/api/v1/web/admin`. Każdy wymaga obecnego `IAdministratorAccess`; mutacje dodatkowo CSRF i UUID `Idempotency-Key`. Odpowiedzi `no-store`. Autor, daty i wersja wynikowa pochodzą z backendu.

| Endpoint | Zachowanie |
| --- | --- |
| `POST /collections` | Tworzy szkic z `displayName`, `slug`; `201` albo `200` przy ponowieniu |
| `GET /collections` | Lista z paginacją i opcjonalnym statusem, także szkice i ukryte |
| `GET /collections/{id}` | Metadane, status, wersja i liczba przypisań |
| `PUT /collections/{id}` | Pełne metadane/status i `expectedVersion`; waliduje publikację |
| `GET /collections/{id}/products` | Paged dane przypisań również do ukrytych produktów; wersja kolekcji i minimalna pełna lista `productIds` do edycji całego zestawu |
| `PUT /collections/{id}/products` | `{expectedVersion,productIds:[...]}` zastępuje **cały skład**, nie tylko stronę; `200` |

Listy administracyjne: `page=1`, `pageSize=20`, zakres 1–100. Błędy: `400` dane, `401`/`403` dostęp/CSRF, `404` zasób, `409` slug/wersja/klucz/stany, `503` niedostępność. `collection_operations` przechowuje wynik także dla pustego lub niezmienionego zestawu. Zakres odpowiedzi i limity body opisać w OpenAPI przy implementacji.

## 4. Publiczny odczyt i wyszukiwanie

Publiczne kolekcje to wyłącznie `Published`; szkic i ukryta kolekcja zwracają `404`. Kolekcje nie tworzą drzewa. Selektory id/slug są wzajemnie wykluczające, jak w istniejącym odczycie produktu/kategorii.

| Endpoint | Zachowanie |
| --- | --- |
| `GET /api/v1/web/collections` | Stronicowana lista kolekcji, podstawowe metadane; bez pełnych produktów |
| `GET /api/v1/web/collections/details?id=...` lub `?slug=...` | Jedna kolekcja po dokładnie jednym selektorze |
| `GET /api/v1/web/products/by-collection?collectionId=...` lub `?slug=...` | Produkty kolekcji; dotychczasowe lekkie kafelki, `q`, sort, cena i paginacja według zasad istniejących list |
| `POST /api/v1/web/products/search` | Dodatkowe opcjonalne `collectionId` albo `collectionSlug`; obecne cechy, ceny, sort i `availableFilters` działają w tym zakresie |
| `GET /api/v1/web/products/filter-options` | Opcjonalne jednoznaczne `collectionId` albo `collectionSlug`; dotychczasowe parametry kategorii zachowują znaczenie |

Przyjęta reguła: jednoczesna kategoria i kolekcja oznaczają przecięcie zbiorów; kategoria nadal obejmuje poddrzewo. Klucze i wartości cech pozostają dynamiczne. Nie dodajemy osobnego POST tylko do prostego wyboru kolekcji.

Wydzielić współdzielony resolver zakresu i zapytania listy z `ProductsController` tylko w stopniu potrzebnym do ponownego użycia. Nie kopiować filtrowania, rankingu, paginacji ani zapytań liczników. Sprawdzić, że dopasowanie SKU/cech/ceny jest na tym samym wariancie i że metadane obniżki kafelka pochodzą z reprezentatywnego pasującego wariantu. Dodanie produktów do kolekcji nie zmienia listy ogólnego katalogu poza nową możliwością zawężenia.

## 5. Rozszerzenie istniejących promocji

Zachować obecne trasy `/admin/promotions`: nowy zakres nie potrzebuje drugiego kontrolera promocji. `POST`/`PUT` szkicu otrzymuje opcjonalny `scope` (domyślnie `product` dla kompatybilności) i odpowiedni identyfikator. Przykład nowego szkicu:

```json
{
  "scope": "collection",
  "collectionId": "uuid-kolekcji",
  "name": "Letnia kolekcja -20%",
  "discountType": "percentage",
  "discountValue": 20
}
```

Stare żądanie z samym `productId` pozostaje poprawne. Odrzucać oba identyfikatory naraz oraz identyfikator niezgodny ze scope. Listę promocji rozszerzyć o filtr `scope`/`collectionId`, pozostawiając `productId` jako filtr bezpośredniego celu promocji, a nie wszystkich produktów z utrwalonych udziałów.

W `PromotionService` oddzielić wybór zakresu od wyliczenia podglądu: produkt i kolekcja prowadzą do wspólnej listy wariantów i tych samych kalkulatorów. Historię cen/oferty pobierać zbiorczo dla objętych wariantów, bez uruchamiania osobnego podglądu/zapytań dla każdego produktu. Warianty deduplikować przed walidacją i zapisem.

Podgląd kolekcji zwraca wersję promocji, `collectionVersion`, pełne `expectedPrices`, stronicowane warianty z `productId`/nazwą produktu i diagnozę wykluczeń. `canActivate` dotyczy całego zakresu, nie strony. Aktywacja wymaga `expectedVersion`, `expectedCollectionVersion` i dokładnego zestawu wariantów z wersjami cen. Zmiana metadanych/składu kolekcji, statusów produktów/wariantów albo ceny wymaga ponownego podglądu, jeśli zmieni wersję/skład/walidację.

Aktywacja ponownie rozwiązuje zakres pod wspólną blokadą; wszystko zapisuje w jednej transakcji. Zakończenie nie rozwiązuje kolekcji ponownie i nie wymaga jej aktualnej wersji. Korzysta z zapisanych `promotion_variants`. Konflikty są diagnozowane w podglądzie, a indeks aktywnego udziału nadal chroni zapis w bazie.

**Kompatybilność ponowień:** obecny hash żądania serializuje konkretny zestaw pól z `productId`. Samo dodanie `scope`/`collectionId` do tego JSON zmieniłoby hashe dawnych żądań. Dla znormalizowanego zakresu `product` zachować dotychczasowy fingerprint, również dla komend start/koniec. Kolekcje otrzymują jednoznaczny nowy format fingerprintu. Sprawdzić odtwarzanie zapisanych odpowiedzi sprzed migracji z brakującymi nowymi polami; nie przepisywać niezmiennych `promotion_operations`.

Odtwarzanie operacji musi poprzedzać walidację bieżącego stanu celu, tak jak dziś. Późniejsza edycja szkicu lub zakończenie akcji nie mogą unieważnić wcześniejszego poprawnego ponowienia. Format fingerprintu kolekcji ma jawne oznaczenie wersji; nie wyznaczać formatu historycznej operacji wyłącznie z obecnej, edytowalnej reguły promocji.

### Rozmiar i czas operacji

Pełna lista oczekiwanych wariantów jest celowym zachowaniem kontraktu. Limit pierwszego wydania: do 3000 wariantów przy aktywacji kolekcji, z czytelnym błędem podglądu przed kosztownym ładowaniem historii oraz ograniczeniem body adekwatnym do tego zestawu. Limit jest wdrożony; zmierzono akcję 1500 wariantów i przetestowano odrzucenie 3001. Czasu wykonania dokładnie 3000 wariantów nie zmierzono. Zakończenie już uruchomionej akcji musi działać nawet po późniejszym obniżeniu limitu. Nie dodawać przetwarzania w tle ani częściowej aktywacji w tym etapie.

## 6. Seeder i dokumentacja

W tym samym wdrożeniu rozszerzyć [seeder](demo-seed.md) według [zasad utrzymania](seeder-maintenance.md):

- Kolekcja opublikowana z produktami z różnych kategorii; produkt w dwóch kolekcjach; szkic/pusta/ukryta kolekcja; produkt niewidoczny w przypisaniach administracyjnych.
- Przykładowa aktywna i zakończona promocja kolekcji, bez konfliktu z obecnymi promocjami produktów; oddzielny szkic do próby konfliktu. Syntetyczna historia oferty nadal jawnie `source=demo`.
- Reset obejmuje trzy nowe tabele: łącznie 17 w `TRUNCATE ... RESTRICT` i `ANALYZE`, z tą samą blokadą i transakcją. Nie dotyka kont, administratorów ani `auth`.
- Nowe liczniki `collections`, `collectionProductLinks`, `collectionOperations`; `added` pozostaje sumą wszystkich. Dziennik operacji kolekcji w seederze może mieć 0 — seeder tworzy stan, a nie fikcyjne działania administratora. Mimo tego tabela musi należeć do resetu i licznika.
- Seeder ma stały mały zestaw scenariuszy kolekcji bez nowej opcji body seedera w pierwszej wersji. Sprawdzić największy zestaw i reset po zapisach obu serwisów.
- Po implementacji uaktualnić `api-contract.md`, `database.md`, `catalog-model.md`, `pricing-model.md`, `demo-seed.md`, `architecture.md`, `testing.md` i status 3.2 w planie. Kontrakt API zaktualizowano razem z implementacją.

## 7. Testy, pomiar i wdrożenie

- Jednostkowe: nazwa/slug/status, selektory celu, walidacja wersji i list produktów, fingerprinty, publiczna widoczność kolekcji niezależna od widoczności produktu.
- PostgreSQL: FK/unikalność, publikacja, wersje i zmiana składu także przez SQL, ponowienia, konflikt slugu, równoczesna edycja składu kontra aktywacja, produkt kontra kolekcja, dwie nakładające się kolekcje, rollback po rzeczywistym błędzie zapisu, koniec po usunięciu przypisania/ukryciu.
- Migracja z danymi: dotychczasowe aktywne/zakończone promocje, kwoty, udziały, audyt i ponowienia starych komend po aktualizacji; brak ingerencji w stare odniesienia i historię oferty.
- Publiczne odczyty: id/slug, kolekcja + kategoria, paginacja bez duplikatów, cechy na tym samym wariancie, filtry ceny i liczniki w kolekcji; regresja katalogu i promocji produktu.
- HTTPS: administrator tworzy kolekcję, przypisuje produkty, publikuje, uruchamia/kończy promocję i ponawia żądania. Dwa prawdziwe konta Auth oraz izolowany lokalny katalog jak w `tests/smoke/promotions.ps1`; zwykły klient nie zmienia danych.
- Pomiar na katalogu 5000 produktów: kolekcje 10/100/500 produktów, liczba wariantów/cen, p50/p95 podglądu/startu/końca, liczba zapytań i oczekiwanie na wspólną blokadę podczas równoczesnego zapisu. Odczyty porównać na tym samym zbiorze przed/po; nie zwiększać timeoutów zamiast zbadać regresję.
- Build, pełne testy z PostgreSQL, zgodność modelu EF z migracją i przegląd SQL; dopiero potem zastosowanie migracji w uzgodnionej bazie, bez seedowania danych rzeczywistych. Przed kodowaniem ponownie użyć `protected-area-check`.

## Kolejność prac i odbiór

- [x] **K1 — reguły:** przyjąć zakres pierwszej wersji, zasady widoczności, zamrożenia, konfliktów i roboczy limit.
- [x] **K2 — baza:** encje, mapowania, migracja i zgodność istniejących promocji/ponowień; seeder aktualizowany równolegle z modelem.
- [x] **K3 — administracja kolekcji:** tworzenie, metadane/status, pełny skład, wersje, ponowienia i testy dostępu.
- [x] **K4 — odczyt:** lista/szczegóły/produkty kolekcji oraz zakres wyszukiwania i filtrów, z reużyciem obecnych zapytań.
- [x] **K5 — promocja kolekcji:** wspólny podgląd, kontrola wersji kolekcji, atomowy start i koniec z utrwalonego zakresu.
- [x] **K6 — odbiór:** migracja na danych, pełne testy, scenariusze seedera, pomiar większej kolekcji, HTTPS i dokumentacja.

Warunek ukończenia: administrator może przygotować kolekcję i uruchomić rabat dla jej oferty; publiczne ceny są spójne; edycja składu nie zmienia uruchomionej akcji; zakończenie i ponowienia nie gubią historii; dotychczasowa promocja produktu nadal działa. To zamyka zakres kolekcji w 3.2, nie cały moduł 3 ani promocje całego katalogu.

Kontrola planu przez `protected-area-check` z 01.11.2026: lista `docs/protected-areas.md` jest pusta. Następnie użytkownik zlecił realizację. Przed edycją ponownie sprawdzono tę listę; pozostała pusta. Źródłem prawdy o wdrożeniu są kod i migracja `20261101124223_AddCollections`.

## Wynik odbioru — 01.11.2026

Migracja `20261101124223_AddCollections` została zastosowana lokalnie i w skonfigurowanym Supabase. Próba aktualizacji zachowała wszystkie pola 45 029 cen i 41 zapisanych operacji promocji; dawny fingerprint i odpowiedzi przeszły testy zgodności. Pełny build ma 0 błędów/ostrzeżeń, 132 testy z PostgreSQL zaliczone bez pominięć, model EF zgodny z migracją. Osobno wykonano 32 sprawdzenia HTTPS na dwóch prawdziwych kontach Supabase Auth i lokalnej bazie katalogu; oba konta usunięto po wylogowaniu i zweryfikowano ich brak. Nie wykonywano seedowania Supabase.

Zmierzono kolekcje 10/100/500 produktów na katalogu 5000 produktów. Dla 500 produktów / 1500 wariantów p95 podglądu wyniosło 124,5 ms, aktywacji 779,0 ms, końca 412,1 ms. Równoległy zapis czekał około 694,1 ms na wspólną blokadę. Nie ma zapytania per wariant/produkt; większa liczba komend wynika z batchy zapisu. To lokalna mała próba, bez prognozy opóźnień Supabase. Szczegóły i porównanie odczytu przed/po: [audyt](catalog-performance-audit.md#kolekcje--pomiar-01112026).

Granice pozostają jawne: limit aktywacji 3000 wariantów kolekcji, brak automatycznego włączania nowych produktów do aktywnej akcji, brak fizycznego usuwania i obrazków kolekcji, brak promocji całego katalogu/harmonogramów/łączenia rabatów. Odbiór HTTP nie zastępuje przeglądarkowych testów przyszłego frontendu ani testu katalogowego połączenia HTTP→zdalny Supabase. Instrukcje i przykłady są w [kontrakcie](api-contract.md#kolekcje-i-promocje-kolekcji--01112026).

Aktualizacja 02.11.2026: [dynamiczny katalog i kategorie](promotion-extension-plan.md) są osobnym, wdrożonym w kodzie rozszerzeniem. Powyższy odbiór zachowuje historyczny zakres wydania kolekcji; kolekcje nadal mają zamrożony skład, limit 3000 wariantów i ten sam format ponowień. Harmonogramy i łączenie rabatów pozostają poza zakresem obu wydań.
