# Magazyn wariantów i rezerwacje

Cel: opisać stan magazynu, rezerwacje, minimalne zamówienie i granice pierwszej implementacji. Powiązane dokumenty: [katalog](catalog-model.md), [API](api-contract.md), [testy](testing.md).

## Stan kodu — 01.11.2026

Istnieją stan wariantu, korekty administratora, rezerwacje przy składaniu zamówienia, anulowanie, wygaszanie i serwerowe rozliczenie sprzedaży. Zamówienie utrwala nazwę, SKU, cechy, wariant, ilość, cenę oraz identyfikator historycznej ceny. Klient może odczytać własną historię i anulować oczekujące zamówienie. To minimalny wycinek potrzebny do magazynu: bez adresu, dostawy, kosztów dostawy, Tpay, zwrotów i administracyjnego API zamówień.

Migracji `20261101191527_AddInventoryReservations` nie zastosowano w zdalnym Supabase w tym zadaniu. Odbiór lokalny i odtwarzanie testów opisuje [testing.md](testing.md).

## Ilość i dostępność

Każdy wariant ma zero albo jeden `VariantStock`, z własnym UUID, datami i wersją. Brak rekordu oznacza nieznany stan, nie zero, i blokuje zakup. Zero blokuje dodanie/zwiększenie pozycji oraz zamówienie. Te reguły istnieją już w koszyku; poprzedni dokument pozostawiał sprzedaż przy zerze jako pytanie otwarte. Podstawowa relacja i znaczenie braku stanu wynikają z ustalenia użytkownika z 21.10.2026.

`QuantityOnHand` oznacza stan ewidencyjny magazynu sprzedażowego. Pierwsza implementacja pomniejsza go po potwierdzeniu płatności, więc nie obejmuje już sprzedanych sztuk oczekujących na wysyłkę. To doprecyzowanie wcześniejszego roboczego określenia „ilość fizyczna”; ewidencja fizycznej wysyłki należy do przyszłego modułu realizacji.

```text
quantityAvailable = quantityOnHand − suma aktywnych rezerwacji z expiresAt > evaluatedAt
```

Suma jest wyliczana zbiorczo, bez osobnego licznika rezerwacji. `quantityOnHand` zachowuje znaczenie, nowe `quantityAvailable` służy ocenie zakupu. Oba są `null` przy nieznanym stanie. Szczegóły produktu, koszyk i ulubione stosują wspólną regułę; listy katalogu nadal nie obiecują ilości. Administracyjne odczyty magazynu i wariantów dodatkowo zwracają `quantityReserved`. Rezerwacja nie zwiększa wersji ilości ewidencyjnej; korekta sprawdza rezerwacje ponownie pod blokadą.

## Reguły pierwszej wersji

Podstawa zakresu: plan przedstawiony użytkownikowi i polecenie rozpoczęcia wdrożenia z 01.11.2026. Poniższe czasy są domyślnymi ustawieniami implementacji, nie wymaganiami operatora płatności.

- Koszyk i otwarcie checkoutu nie rezerwują. Rezerwacja powstaje z zamówieniem zalogowanego klienta.
- Zamówienie obejmuje cały koszyk konta. Wymaga zgodnego UUID zakresu, wersji i akceptacji każdej pozycji: wariant, ilość, `priceChangeId`, `unitPrice`.
- Backend ponownie sprawdza publikację, cenę PLN i dostępność. Zmiana ceny daje `price_changed`; nie powstaje częściowe zamówienie ani rezerwacja.
- Zapis zamówienia, pozycji i rezerwacji oraz opróżnienie koszyka są jedną transakcją. Nie ma przenoszenia koszyka gościa.
- Termin to domyślnie 15 minut, `Inventory:ReservationMinutes` 1–120. Czas ocenia PostgreSQL po uzyskaniu blokad. Ponowienie nie przedłuża terminu.
- `Idempotency-Key` jest UUID unikalnym dla konta. Identyczne ponowienie zwraca to samo zamówienie, aktualny status, pierwotny termin i `replayed:true`, także po opróżnieniu koszyka. Inna treść daje konflikt. Kolejność pozycji nie zmienia treści komendy.
- Korekta nie może ustawić ilości poniżej aktywnych rezerwacji. Anulowanie zwalnia rezerwację bez pomniejszenia stanu i nie odtwarza opróżnionego koszyka.
- Po terminie rezerwacja nie blokuje dostępności, nawet przed pracą workera. Odczyt zamówienia pokazuje efektywny status `Expired` przed jego utrwaleniem.
- Zaufane potwierdzenie płatności przed terminem zmienia rezerwację na `Consumed` i pomniejsza stan raz. Obowiązuje snapshot ceny zamówienia.
- Ten sam UUID zdarzenia płatności musi wskazywać to samo zamówienie, kwotę i walutę. Następne zdarzenie dla opłaconego zamówienia nie odejmuje ponownie stanu. Rozpoznanie faktycznego podwójnego obciążenia wymaga przyszłego adaptera operatora.
- Płatność po terminie lub anulowaniu zapisuje `PaymentReview` bez pomniejszenia stanu i bez wznowienia rezerwacji. Zwrot pieniędzy wymaga modułu płatności.
- Nieudana próba płatności nie zmienia rezerwacji: pozostaje ona do terminu lub anulowania.
- Ceny zerowe są dozwolone zgodnie z katalogiem. Serwerowe rozliczenie przyjmuje zero; automatyczny checkout darmowych zamówień nie ma jeszcze osobnej ścieżki API.

## Model danych

| Tabela `app` | Znaczenie |
| --- | --- |
| `variant_stock` | Istniejąca nieujemna ilość, unikalny wariant, wersja i daty |
| `orders` | Konto, klucz/hash komendy, status, suma towarów PLN i daty |
| `order_items` | Niezmienny snapshot nazwy, SKU, cech, wariantu, ceny i ilości |
| `stock_reservations` | Jedna rezerwacja zamówienia, status, stały termin i daty |
| `stock_reservation_items` | Ilości wariantów; wymagają istniejącego rekordu stanu |
| `stock_movements` | Niezmienna ilość przed/po sprzedaży, najwyżej jeden wpis na pozycję rezerwacji |
| `inventory_payment_receipts` | Niezmienny wynik zaufanego zdarzenia płatności, unikalny UUID zdarzenia |

Statusy zamówienia: `PendingPayment`, `Paid`, `Cancelled`, `Expired`, `PaymentReview`. Statusy rezerwacji: `Active`, `Consumed`, `Released`, `Expired`. Terminalna rezerwacja nie wraca do aktywności. Dane zakupowe używają FK `RESTRICT`, bez kasowania kaskadowego. Zamówienie blokuje bezpośrednie usunięcie konta Auth; przyszły proces usunięcia/anonimizacji musi świadomie obsłużyć historię.

Nowe tabele mają RLS i odebrane uprawnienia `PUBLIC`, `anon`, `authenticated`, `service_role`. Nie są wystawiane przez Data API. Backend nadal sprawdza tożsamość i właściciela. Funkcje nie używają `SECURITY DEFINER`.

## Transakcje i PostgreSQL

`InventoryOrderService` zachowuje blokadę katalogu `(20261031,3)`, wymaganą przez istniejące triggery. Pobiera ją przed współdzieloną blokadą shopping, wierszem koszyka i stanami wariantów pobieranymi w kolejności UUID przez `FOR UPDATE`. Pozostałe zapisy magazynu też zaczynają od blokady katalogu. W transakcji nie ma HTTP ani wysyłki wiadomości.

Pierwsza wersja serializuje zapisy także między różnymi wariantami. Zmiana wymaga wspólnego projektu dla promocji, publikacji, administracji i seedera oraz pomiarów; nie wystarczy usunąć blokady tylko z checkoutu.

Odroczone ograniczenia sprawdzają przy commit zgodność zamówienia, pozycji, rezerwacji, sumy, historycznej ceny i rozliczenia. Chronią też przed SQL obniżającym stan poniżej aktywnych rezerwacji. Triggery blokują zmianę snapshotów, historii, tożsamości i terminu oraz niedozwolone przejścia statusów. Błąd wycofuje także opróżnienie koszyka.

`ReservationExpiryWorker` przetwarza do 100 rezerwacji na transakcję co 30 sekund (`Inventory:ExpiryIntervalSeconds` 1–300). Kilka instancji serializuje się tą samą blokadą. Po restarcie wybierane są stare terminy. Poprawność dostępności i rozliczenia nie zależy od punktualności workera.

## Płatności, seeder i dalszy odbiór

`ConfirmPaymentAsync` nie ma publicznego endpointu. Przyszły adapter Tpay musi najpierw zweryfikować autentyczność zdarzenia, mapowanie zamówienia, UUID zdarzenia, kwotę i walutę. Powrót przeglądarki nie może rozliczać magazynu. Zwroty, przywracanie stanu i wysyłka pozostają osobnym zakresem.

Seeder zachowuje 18 liczników katalogu. Nowe dane zakupowe nie są seedowane, bo należą do kont; przygotowują je fixture i API. Reset przy jakimkolwiek zamówieniu daje `409 seed_blocked_by_orders` przed zmianą danych. Sześć nowych pustych tabel należy do jawnego `TRUNCATE ... RESTRICT`, aby PostgreSQL dopuścił reset katalogu bez `CASCADE`. Uzasadnienie wyjątku: [seeder-maintenance.md](seeder-maintenance.md).

Przed i podczas wdrożenia sprawdzono `protected-area-check`: lista chroniona jest pusta. Pozostają migracja i odbiór zdalnej bazy, frontend i rzeczywisty adapter płatności. Lokalny test nie zamyka tych etapów.
