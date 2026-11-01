# Dane demonstracyjne katalogu

Cel: opisać ręcznie uruchamiany seeder danych demonstracyjnych potrzebny podczas rozwoju katalogu, bez automatycznego dopisywania danych przy starcie API lub migracji.

Zasady rozbudowy seedera przy kolejnych zmianach modelu bazy są w [seeder-maintenance.md](seeder-maintenance.md).

Tabela `app.administrators` jest celowo wyłączona z seedowania i resetu: zawiera uprawnienia kont. Seeder nie tworzy fikcyjnych kont, nie nadaje uprawnień i nie usuwa istniejących administratorów. Liczniki katalogu nie obejmują tej tabeli; test PostgreSQL potwierdza jej zachowanie podczas resetu. Pierwszy dostęp nadaje się według [instrukcji administratora](administrator-access.md).

Podstawowe historyczne stany cen seeder tworzy z `operation_key`, `changed_by_user_id` i `previous_price_change_id` równymi null. Nie są to działania administratora, więc nie tworzy fikcyjnych kont ani kluczy operacji; nowe scenariusze promocji mają osobne powiązania i liczniki opisane poniżej. Zgodność zapisu i ponownego resetu po użyciu `VariantPriceService` sprawdza test PostgreSQL. Powód wyjątku i zakres migracji są w [modelu cen](pricing-model.md#wdrożony-pierwszy-etap--29102026).

## Stan potwierdzony w kodzie

Aktualizacja 01.11.2026 — koszyk i ulubione: reset obejmuje **20 tabel** (18 katalogowych, w tym równolegle dodany `catalog_operations`, oraz `cart_items`, `favorite_items`). Przed TRUNCATE zwiększa wersje koszyków i list mających pozycje. Zachowuje `shopping_states`, `favorite_lists`, profile, konta i administratorów. Wymaga nowej migracji `AddShoppingCartsAndFavorites` w używanej bazie. Nie tworzy danych klientów ani tokenów gościa; `created` i `added` pozostają licznikami 18 typów katalogu, w tym `catalogOperations` (domyślnie 0). Blokada wyłączna `(20261101,4)` wyklucza równoczesne operacje modułu na czas resetu. Test PostgreSQL sprawdza zachowanie właścicieli, wzrost wersji, ponowny seed i wyścig resetu z zapisami.

- `POST /api/v1/web/seed` jest mapowany tylko przy `ASPNETCORE_ENVIRONMENT=Development` i `DemoSeed:Enabled=true`. Gdy brakuje `DemoSeed:Key` lub ma on mniej niż 32 znaki, API nie startuje z włączonym seederem.
- Żądanie wymaga nagłówka `X-Demo-Seed-Key` równego skonfigurowanemu kluczowi. Brak lub niepoprawny klucz zwraca `404`; sukces zwraca `200` z sumą `added` oraz obiektem `created`. W `created` są liczby rekordów utworzonych podczas tego wywołania: `categories`, `products`, `variants`, `attributeKeys`, `attributePairs`, `productCategoryLinks`, `variantAttributeLinks`, `stocks`, `priceChanges`, `offerEvents`, `promotions`, `promotionVariants`, `priceReductions`, `promotionOperations`, `collections`, `collectionProductLinks`, `collectionOperations`, `catalogOperations`. Suma pól `created` równa się `added`.
- Body jest opcjonalne. Pola `categories`, `products` i `variants` niezależnie przyjmują `low`, `med`/`medium` albo `high`; pominięte `categories` i `products` mają wartość `medium`, a `variants` — `low`. `variants` oznacza docelową liczbę wariantów na produkt (bazowa koszulka zawsze ma co najmniej dwa). Niepoprawna wartość zwraca `400`.
- Poziomy określają zakresy, z których przy **każdym wywołaniu** losowany jest cel (obie granice włącznie):

  | Typ danych | `low` | `medium` | `high` |
  | --- | ---: | ---: | ---: |
  | Kategorie łącznie | 10–20 | 25–40 | 50–80 |
  | Produkty łącznie | 50–100 | 200–300 | 400–500 |
  | Warianty na produkt | 1–2 | 3–4 | 5–6 |

  Kategorie i produkty mają po jednym celu na żądanie; warianty są losowane osobno dla każdego produktu. Każde wywołanie zaczyna od pustego katalogu, więc nowy poziom zastępuje poprzednio wygenerowany zestaw. Zestaw bazowy ma 3 kategorie, 2 produkty, 2 warianty koszulki i 1 czapki; dodatkowe rekordy uzupełniają go do wylosowanych celów. Liczba wariantów na produkt pozostaje w zakresie 1–6.
- `DemoCatalogSeeder` zachowuje ręcznie ustalony zestaw: korzenie `Odzież demo` i `Akcesoria demo`, podkategorię `Koszulki demo`, produkt `Koszulka demo` oraz `Czapka demo`. Wyższe poziomy dopisują produkty, kategorie i warianty. Seeder dodaje pary cech, przypisania kategorii, stany wariantów i przykładową historię cen w PLN. Zdjęcia pozostają puste, ponieważ nie ma jeszcze integracji ze Storage.
- Seeder zapisuje reset i nowe dane w jednej transakcji. Czyści wszystkie rekordy z 20 tabel `app`: `cart_items`, `favorite_items`, `catalog_operations`, `collections`, `collection_products`, `collection_operations`, `price_reductions`, `promotion_variants`, `promotion_operations`, `promotions`, `variant_offer_events`, `variant_price_changes`, `variant_stock`, `variant_attribute_pairs`, `product_categories`, `product_variants`, `products`, `attribute_pairs`, `attribute_keys`, `categories`. Obejmuje to także dane niewytworzone przez seeder i wcześniejszą historię cen. Nie czyści stanów/list właścicieli, `app.user_profiles`, tabel `auth` ani historii migracji. `TRUNCATE ... RESTRICT` nie rozszerza zakresu na przyszłe tabele zależne; gdy taka zależność powstanie, reset zakończy się błędem i wycofa transakcję.
- Po resecie seeder buduje cały zestaw encji i powiązań w pamięci, bez zapytań o istnienie rekordów. UUID są nadawane w aplikacji. `SaveChangesAsync` zapisuje szkice oraz zależne kategorie, cechy, warianty, powiązania, stany i ceny; EF Core ustala kolejność poleceń według relacji. Po wyczyszczeniu śledzenia encji osobne zbiorcze UPDATE publikują kolejno warianty, produkty i kolekcje, bez konfliktów wersji podniesionych przez wyzwalacze grafu. Błąd na dowolnym etapie wycofuje reset i nowe dane. Dwa równoczesne wywołania serializuje blokada transakcyjna PostgreSQL.
- Przed zatwierdzeniem transakcji seeder wykonuje `ANALYZE` dla 20 tabel resetu. Po pełnym `TRUNCATE` i większym zapisie aktualne statystyki pozwalają PostgreSQL dobrać plan wyszukiwania do nowej wielkości katalogu. Gdy `ANALYZE` zawiedzie, transakcja seedera zostaje wycofana.
- `TRUNCATE` omija wyzwalacz blokujący `DELETE` w historii cen. Ten pełny reset jest przeznaczony wyłącznie do developerskich baz z danymi do odtworzenia; nie należy kierować endpointu na bazę z rzeczywistą historią cen.

## Scenariusze promocji (31.10.2026)

Produkty `demo-produkt-03` i `demo-produkt-04` mają aktywną promocję procentową/kwotową, `demo-produkt-05` — zakończoną. `demo-produkt-06` ma znaną krótką ofertę, a `demo-produkt-07` niekompletną historię blokującą aktywację. `demo-czapka` nadaje się do pełnego testu utworzenia promocji. Dawny rabat koszulki pozostaje bez modelu promocji (`reduction=null`); nie jest automatycznie uznawany za zweryfikowaną obniżkę.

Scenariusze 03–07 istnieją na każdym obecnym poziomie liczby produktów. Nie mają osobnych poziomów w body: ich liczba jest stała, natomiast udziały, obniżki i ceny zależą od liczby wariantów tych produktów. To celowy wybór: reprezentatywne scenariusze zamiast masowego tworzenia takich samych promocji. Bazowy zestaw dwóch produktów używany w testach jednostkowych nie zawiera tych pięciu dodatkowych produktów.

Nowe liczniki: `promotions` → `app.promotions`, `promotionVariants` → `app.promotion_variants`, `priceReductions` → `app.price_reductions`, `promotionOperations` → `app.promotion_operations`, `offerEvents` → `app.variant_offer_events`. `offerEvents` obejmuje również rekordy utworzone przez triggery i jest odczytywany przed zatwierdzeniem transakcji, pod blokadą resetu. Suma wszystkich 17 liczników daje `added`.

Zdarzenia z cofniętą datą mają `source=demo`; służą wyłącznie odtwarzalnym danym syntetycznym. Operacje demonstracyjne mają `kind=demo` i brak autora/klucza; ceny promocji wskazują te operacje oraz poprzednią cenę. Seeder nie tworzy fikcyjnych administratorów. Reset po rzeczywistej aktywacji i zakończeniu został sprawdzony w testach PostgreSQL.

## Kolekcje — 01.11.2026

Kolekcje: `demo-kolekcja` zawiera czapkę, produkt 11 i ukryty produkt 10; `demo-pomysly-na-prezent` czapkę i produkt 12. Powstają też pusty szkic i ukryta kolekcja. `demo-kolekcja-aktywna` obejmuje produkty 8/13 z aktywną promocją, a `demo-kolekcja-zakonczona` produkty 9/14 z zakończoną. Produkty 10 i ich warianty są pomijane w odczycie publicznym zgodnie ze statusem produktu. Scenariusze wymagające wyższego indeksu są pomijane w mniejszych zestawach używanych w testach; wszystkie występują na normalnych poziomach endpointu.

Nowe liczniki: `collections`, `collectionProductLinks`, `collectionOperations`. Ostatni wynosi w seedzie 0: nie tworzymy fikcyjnych operacji administratora. Mimo tego tabela należy do resetu, bo rzeczywiste operacje API mogą już w niej istnieć. Publikacja kolekcji następuje po zapisaniu produktów, cen i przypisań; przed zmianą statusu EF odczytuje wersję nadaną przez triggery przypisań. Nie dodano nowego pola body seedera — generowany jest stały zestaw scenariuszy.

## Ręczne uruchomienie

W pierwszej konsoli, w katalogu projektu, wygeneruj klucz i zapisz go w lokalnych .NET user-secrets; nie wpisuj klucza do repozytorium:

```powershell
$seedBytes = New-Object byte[] 32
$rng = [Security.Cryptography.RandomNumberGenerator]::Create()
try { $rng.GetBytes($seedBytes) } finally { $rng.Dispose() }
$seedKey = [Convert]::ToBase64String($seedBytes)
dotnet user-secrets set "DemoSeed:Key" $seedKey
dotnet user-secrets set "DemoSeed:Enabled" "true"
```

W drugiej konsoli uruchom API:

```powershell
dotnet run --launch-profile https
```

Wróć do pierwszej konsoli, w której nadal jest zmienna `$seedKey`, i wywołaj:

```powershell
Invoke-RestMethod -Method Post -Uri "https://localhost:3100/api/v1/web/seed" -Headers @{ "X-Demo-Seed-Key" = $seedKey }
```

Przykład z osobnymi poziomami (pozostałe pola można pominąć):

```powershell
Invoke-RestMethod -Method Post -Uri "https://localhost:3100/api/v1/web/seed" `
  -Headers @{ "X-Demo-Seed-Key" = $seedKey } -ContentType "application/json" `
  -Body '{"categories":"low","products":"medium","variants":"high"}'
```

## Odpowiedź endpointu

Po udanym resecie i zapisie API zwraca `200` z dokładnie dwoma polami na najwyższym poziomie:

| Pole | Znaczenie |
| --- | --- |
| `added` | Łączna liczba wszystkich utworzonych wierszy, również powiązań i wpisów historii cen. Jest sumą pól `created`. |
| `created` | Liczniki wierszy utworzonych w poszczególnych tabelach podczas tego wywołania. |

Pola `created` odpowiadają kolejno tabelom: `categories` → `app.categories`, `products` → `app.products`, `variants` → `app.product_variants`, `attributeKeys` → `app.attribute_keys`, `attributePairs` → `app.attribute_pairs`, `productCategoryLinks` → `app.product_categories`, `variantAttributeLinks` → `app.variant_attribute_pairs`, `stocks` → `app.variant_stock`, `priceChanges` → `app.variant_price_changes`. Liczniki dotyczą wierszy, więc `added` nie oznacza wyłącznie liczby nowych produktów. Odpowiedź nie podaje liczby rekordów sprzed wywołania: katalog jest czyszczony przed utworzeniem nowego zestawu.

Przykładowa odpowiedź dla jednego z możliwych losowań domyślnych poziomów (25 kategorii, 200 produktów, 1 wariant na produkt poza bazową koszulką):

```json
{
  "added": 2089,
  "created": {
    "categories": 25,
    "products": 200,
    "variants": 201,
    "attributeKeys": 2,
    "attributePairs": 4,
    "productCategoryLinks": 200,
    "variantAttributeLinks": 400,
    "stocks": 201,
    "priceChanges": 212,
    "offerEvents": 602,
    "promotions": 5,
    "promotionVariants": 7,
    "priceReductions": 7,
    "promotionOperations": 7,
    "collections": 6,
    "collectionProductLinks": 10,
    "collectionOperations": 0,
    "catalogOperations": 0
  }
}
```

Nie umieszczaj klucza w skryptach ani logach. Endpoint zapisuje dane w bazie wskazanej przez `ConnectionStrings:SupabaseDatabase`, która lokalnie może wskazywać na projekt Supabase. Przed wywołaniem sprawdź docelową bazę. Po pracy wyłącz endpoint:

```powershell
dotnet user-secrets set "DemoSeed:Enabled" "false"
```

## Weryfikacja i otwarte kwestie

Testy jednostkowe sprawdzają klucz, opcje oraz kompletność i spójność grafu danych dla zestawu bazowego i największego (500 produktów, po 6 wariantów). Test integracyjny wyszukiwania uruchamia seeder na odizolowanym PostgreSQL; warunki uruchomienia są w [testing.md](testing.md). Nie ma jeszcze pomiaru czasu zapisu największego zestawu do bazy. Przed użyciem na bazie z danymi trzeba sprawdzić docelowe połączenie i zakres czyszczonych tabel.
