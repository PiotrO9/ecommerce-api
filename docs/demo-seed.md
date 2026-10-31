# Dane demonstracyjne katalogu

Cel: opisać ręcznie uruchamiany seeder danych demonstracyjnych potrzebny podczas rozwoju katalogu, bez automatycznego dopisywania danych przy starcie API lub migracji.

Zasady rozbudowy seedera przy kolejnych zmianach modelu bazy są w [seeder-maintenance.md](seeder-maintenance.md).

Tabela `app.administrators` jest celowo wyłączona z seedowania i resetu: zawiera uprawnienia kont. Seeder nie tworzy fikcyjnych kont, nie nadaje uprawnień i nie usuwa istniejących administratorów. Liczniki katalogu nie obejmują tej tabeli; test PostgreSQL potwierdza jej zachowanie podczas resetu. Pierwszy dostęp nadaje się według [instrukcji administratora](administrator-access.md).

Po dodaniu metadanych operacji zmiany ceny seeder nadal tworzy historyczne stany cen z `operation_key`, `changed_by_user_id` i `previous_price_change_id` równymi null. Nie są to działania administratora, więc nie tworzy fikcyjnych kont ani kluczy operacji; liczniki pozostają bez zmian. Zgodność zapisu i ponownego resetu po użyciu `VariantPriceService` sprawdza test PostgreSQL. Powód wyjątku i zakres migracji są w [modelu cen](pricing-model.md#wdrożony-pierwszy-etap--29102026).

## Stan potwierdzony w kodzie

- `POST /api/v1/web/seed` jest mapowany tylko przy `ASPNETCORE_ENVIRONMENT=Development` i `DemoSeed:Enabled=true`. Gdy brakuje `DemoSeed:Key` lub ma on mniej niż 32 znaki, API nie startuje z włączonym seederem.
- Żądanie wymaga nagłówka `X-Demo-Seed-Key` równego skonfigurowanemu kluczowi. Brak lub niepoprawny klucz zwraca `404`; sukces zwraca `200` z sumą `added` oraz obiektem `created`. W `created` są liczby rekordów utworzonych podczas tego wywołania: `categories`, `products`, `variants`, `attributeKeys`, `attributePairs`, `productCategoryLinks`, `variantAttributeLinks`, `stocks`, `priceChanges`. Suma pól `created` równa się `added`.
- Body jest opcjonalne. Pola `categories`, `products` i `variants` niezależnie przyjmują `low`, `med`/`medium` albo `high`; pominięte `categories` i `products` mają wartość `medium`, a `variants` — `low`. `variants` oznacza docelową liczbę wariantów na produkt (bazowa koszulka zawsze ma co najmniej dwa). Niepoprawna wartość zwraca `400`.
- Poziomy określają zakresy, z których przy **każdym wywołaniu** losowany jest cel (obie granice włącznie):

  | Typ danych | `low` | `medium` | `high` |
  | --- | ---: | ---: | ---: |
  | Kategorie łącznie | 10–20 | 25–40 | 50–80 |
  | Produkty łącznie | 50–100 | 200–300 | 400–500 |
  | Warianty na produkt | 1–2 | 3–4 | 5–6 |

  Kategorie i produkty mają po jednym celu na żądanie; warianty są losowane osobno dla każdego produktu. Każde wywołanie zaczyna od pustego katalogu, więc nowy poziom zastępuje poprzednio wygenerowany zestaw. Zestaw bazowy ma 3 kategorie, 2 produkty, 2 warianty koszulki i 1 czapki; dodatkowe rekordy uzupełniają go do wylosowanych celów. Liczba wariantów na produkt pozostaje w zakresie 1–6.
- `DemoCatalogSeeder` zachowuje ręcznie ustalony zestaw: korzenie `Odzież demo` i `Akcesoria demo`, podkategorię `Koszulki demo`, produkt `Koszulka demo` oraz `Czapka demo`. Wyższe poziomy dopisują produkty, kategorie i warianty. Seeder dodaje pary cech, przypisania kategorii, stany wariantów i przykładową historię cen w PLN. Zdjęcia pozostają puste, ponieważ nie ma jeszcze integracji ze Storage.
- Seeder zapisuje reset i nowe dane w jednej transakcji. Najpierw czyści wszystkie rekordy z dziewięciu tabel `app`: `variant_price_changes`, `variant_stock`, `variant_attribute_pairs`, `product_categories`, `product_variants`, `products`, `attribute_pairs`, `attribute_keys`, `categories`. Obejmuje to także dane niewytworzone przez seeder i wcześniejszą historię cen. Nie czyści `app.user_profiles`, tabel `auth` ani historii migracji. `TRUNCATE ... RESTRICT` nie rozszerza zakresu na przyszłe tabele zależne; gdy taka zależność powstanie, reset zakończy się błędem i wycofa transakcję.
- Po resecie seeder buduje cały zestaw encji i powiązań w pamięci, bez zapytań o istnienie rekordów. UUID są nadawane w aplikacji. Pierwsze `SaveChangesAsync` zapisuje szkice oraz zależne kategorie, cechy, warianty, powiązania, stany i ceny; EF Core ustala kolejność poleceń według relacji. Drugie `SaveChangesAsync` publikuje produkty i warianty. Błąd na dowolnym etapie wycofuje zarówno reset, jak i nowe dane. Dwa równoczesne wywołania serializuje blokada transakcyjna PostgreSQL.
- Przed zatwierdzeniem transakcji seeder wykonuje `ANALYZE` dla dziewięciu tabel katalogu. Po pełnym `TRUNCATE` i większym zapisie aktualne statystyki pozwalają PostgreSQL dobrać plan wyszukiwania do nowej wielkości katalogu. Gdy `ANALYZE` zawiedzie, transakcja seedera zostaje wycofana.
- `TRUNCATE` omija wyzwalacz blokujący `DELETE` w historii cen. Ten pełny reset jest przeznaczony wyłącznie do developerskich baz z danymi do odtworzenia; nie należy kierować endpointu na bazę z rzeczywistą historią cen.

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
  "added": 1435,
  "created": {
    "categories": 25,
    "products": 200,
    "variants": 201,
    "attributeKeys": 2,
    "attributePairs": 4,
    "productCategoryLinks": 200,
    "variantAttributeLinks": 400,
    "stocks": 201,
    "priceChanges": 202
  }
}
```

Nie umieszczaj klucza w skryptach ani logach. Endpoint zapisuje dane w bazie wskazanej przez `ConnectionStrings:SupabaseDatabase`, która lokalnie może wskazywać na projekt Supabase. Przed wywołaniem sprawdź docelową bazę. Po pracy wyłącz endpoint:

```powershell
dotnet user-secrets set "DemoSeed:Enabled" "false"
```

## Weryfikacja i otwarte kwestie

Testy jednostkowe sprawdzają klucz, opcje oraz kompletność i spójność grafu danych dla zestawu bazowego i największego (500 produktów, po 6 wariantów). Test integracyjny wyszukiwania uruchamia seeder na odizolowanym PostgreSQL; warunki uruchomienia są w [testing.md](testing.md). Nie ma jeszcze pomiaru czasu zapisu największego zestawu do bazy. Przed użyciem na bazie z danymi trzeba sprawdzić docelowe połączenie i zakres czyszczonych tabel.
