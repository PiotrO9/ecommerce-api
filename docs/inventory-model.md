# Wstępny model stanu magazynowego

Cel: opisać powiązanie stanu magazynowego z wariantem oraz pytania, które trzeba rozstrzygnąć przed modelem checkoutu. Model produktu i wariantu jest w [modelu katalogu](catalog-model.md).

## Stan potwierdzony w kodzie

Backend ma encję `VariantStock`, mapowanie EF Core i zastosowaną w Supabase migrację tworzącą `app.variant_stock` z własnym UUID, unikalnym `variant_id`, nieujemnym `quantity_on_hand` oraz datami UTC. Publiczny odczyt szczegółów produktu zwraca ilość wariantu lub `null`, gdy rekord stanu nie istnieje. Od 01.11.2026 są administracyjne `GET/PUT /api/v1/web/admin/variants/{id}/stock`: inicjalizacja i korekta ilości fizycznej z własnym `Version`, autorem, powodem i ilością przed/po w `catalog_operations`. Pierwszy zapis wymaga `expectedVersion:null`, kolejne aktualnej wersji. Nie ma kasowania stanu przez API, rezerwacji ani checkoutu. Nową migrację wersji sprawdzono lokalnie, bez zastosowania w Supabase; [szczegóły i Postman](catalog-management-implementation-plan.md#13-wdrożenie-odbiór-lokalny-i-użycie).

## Ustalenie z właścicielem projektu

Każdy wariant produktu ma mieć możliwość posiadania **własnego stanu magazynowego**. Wariant może mieć zero albo jeden rekord stanu; brak rekordu oznacza, że ilość nie jest dla niego określona, a nie że wynosi `0`. **Backend blokuje zakup wariantu bez rekordu stanu**, ponieważ nie zna jego ilości. Reguła musi być sprawdzana przy składaniu zamówienia, także gdy wariant został wcześniej dodany do koszyka. Ilość jednego wariantu nie jest wspólną ilością produktu ani innych wariantów. Przykład: koszulka `czerwona/S` może mieć stan 5, a `czerwona/M` stan 12.

Rekord stanu ma mieć własne `Id`, `CreatedAt` i `UpdatedAt`. `VariantId` nadal wskazuje wariant; nie jest identyfikatorem rekordu stanu. Źródło: ustalenie z właścicielem projektu z 21 października 2026 r.

## Uzgodniony wstępny kształt rekordu

Stan jest oddzielony od danych opisowych wariantu w relacji zero albo jeden rekord stanu na wariant:

| Tabela `app` | Pola | Znaczenie |
| --- | --- | --- |
| `variant_stock` | `id uuid` PK; `variant_id uuid` NOT NULL UNIQUE FK → `product_variants.id`; `quantity_on_hand integer`; `created_at`, `updated_at` (UTC) | Osobny identyfikator i daty rekordu stanu. UNIQUE na `variant_id` pozwala przypisać wariantowi najwyżej jeden rekord. `quantity_on_hand` roboczo oznacza liczbę fizycznie posiadanych sztuk. |

Roboczo `quantity_on_hand` nie może być ujemne. Wariant ze śledzonym stanem, ale bez fizycznie posiadanych sztuk miałby wartość `0`. Dla ręcznych korekt przyjęto ilość fizycznie posiadaną; rezerwacje wymagają osobnego modelu dostępności.

```mermaid
flowchart LR
    P["PRODUKT<br/>Id"] -->|"1 → co najmniej 1"| V["WARIANT<br/>Id, ProductId, SKU"]
    V -->|"1 → 0 lub 1 rekord stanu"| S["STAN WARIANTU<br/>Id PK, VariantId UNIQUE FK<br/>QuantityOnHand<br/>CreatedAt, UpdatedAt"]
```

Oddzielna tabela ma własną wersję, bez podnoszenia wersji opisowej produktu. Inicjalizację, nieujemność, korekty i ich współbieżność sprawdzają testy PostgreSQL zarządzania ofertą.

## Pytania otwarte

1. Przyjęto ilość fizyczną dla ręcznych korekt. Jak rozdzielić ją od dostępności po dodaniu rezerwacji?
2. Kiedy pomniejszać lub rezerwować ilość: przy rozpoczęciu checkoutu, utworzeniu zamówienia czy potwierdzeniu płatności? Co robić po nieudanej płatności lub porzuconym checkoutcie?
3. Czy dopuszczać sprzedaż przy istniejącym rekordzie ze stanem `0` (przedsprzedaż lub backorder), czy blokować zakup? Brak rekordu zawsze blokuje zakup.
4. Jak chronić ostatnią sztukę przed równoczesnym zakupem? Ręczne korekty, powody i wersjonowanie są wdrożone; nie zastępują kontroli checkoutu.

Te odpowiedzi wyznaczą model rezerwacji i zmian stanu. Samo pole z ilością nie rozwiązuje jeszcze spójności checkoutu.
