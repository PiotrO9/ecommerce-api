# Dynamiczne promocje katalogu i kategorii

Cel: utrwalić wymagania, plan techniczny i wynik wdrożenia rozszerzenia punktu 3.2. Dokument rozpoczęto 01.11.2026; decyzje właściciela z 02.11.2026 zastępują pierwotną propozycję zamrożonego składu katalogu.

Status: uzgodniony zakres DP1–DP6 wdrożony i odebrany lokalnie 02.11.2026. Nie zastosowano nowej migracji w Supabase. Dotychczasowe promocje produktu i kolekcji opisują [promocje produktu](promotion-implementation-plan.md) i [kolekcje](collection-implementation-plan.md).

## Przyjęte wymagania

Źródło: odpowiedzi właściciela projektu w rozmowie o punkcie 3.2 z 02.11.2026.

- Nowe zakresy: cały katalog oraz kategoria z wszystkimi podkategoriami. Produkt przypisany wielokrotnie nie powoduje wielokrotnego rabatu wariantu.
- Opublikowane produkty mające kategorię i ich opublikowane warianty kwalifikują się także przy braku magazynu. Zakres nie obejmuje szkiców ani ukrytej oferty.
- Skład jest dynamiczny: publikacja, ukrycie, przypisania kategorii i przeniesienie drzewa zmieniają kwalifikację w trakcie kampanii.
- Można wykluczyć cały produkt (także jego przyszłe warianty) albo pojedynczy wariant. Wykluczenia można zastępować w szkicu i aktywnej akcji.
- Nakładanie dwóch promocji blokuje nową operację do ręcznego rozwiązania konfliktu. Nie sumujemy rabatów i nie wybieramy automatycznie korzystniejszej kampanii.
- Niepoprawny rabat blokuje nadanie rabatu, a nie publikację/przeniesienie oferty. Dotychczasowa cena pozostaje; powód trafia do konfliktów.
- Pierwszy start obejmuje poprawne warianty, a pominięte trafiają do konfliktów. Gdy wszystkie rabaty są niepoprawne, kampania może być aktywna bez zastosowanych rabatów i oczekiwać na poprawki. Pusty zakres kwalifikacji przy pierwszym starcie zwraca `no_variants`.
- Po poprawieniu danych backend automatycznie sprawdza wariant ponownie i stosuje rabat, jeśli kampania trwa i pozostałe warunki są spełnione. Dotyczy to błędów rabatu; nakładanie promocji nadal wymaga ręcznego rozwiązania.
- Start i koniec są ręczne. Harmonogramy kampanii pozostają oddzielnym zakresem.
- Nie ustalono biznesowego limitu liczby produktów. Istniejący limit 3000 wariantów kolekcji pozostaje bez zmiany; nowe zakresy nie dziedziczą go automatycznie. Obsługiwany rozmiar wymaga pomiaru.

Zachowujemy kalkulator PLN, kwotę/procent, zaokrąglenie, warunki wcześniejszej ceny i niezmienną historię. Wejście po starcie kampanii ma rzeczywistą datę i własne odniesienie; nie dopisujemy obniżki wstecz. Ponowne wejście podlega ponownej walidacji, więc wcześniejsza niska cena tej samej kampanii może spowodować konflikt `not_below_prior_price`.

## Model i migracja

- `promotions`: `scope=catalog|category`, opcjonalny `category_id` FK RESTRICT i CHECK zgodności celu. Stany pozostają Draft/Active/Ended.
- `promotion_variants`: kolejne okresy udziału tej samej pary promocja/wariant są dozwolone. Zachowany unikalny aktywny udział wariantu między wszystkimi kampaniami. Każdy okres ma własny `PriceReduction`.
- `promotion_exclusions`: UUID, promocja, dokładnie jedno z product_id/variant_id, data; unikalne wykluczenie w danym zakresie.
- `promotion_conflicts`: UUID, promocja, wariant, opcjonalna konkurująca promocja, kod, daty utworzenia/aktualizacji/rozwiązania. Maksymalnie jeden otwarty rekord pary promocja/wariant/kod. Zamknięte wpisy zachowujemy.
- `promotion_reconciliation`: pojedynczy trwały znacznik dirty i termin następnej oceny. Nie jest harmonogramem kampanii.
- `promotion_operations`: jawny systemowy rodzaj `dynamic_reconcile`, bez fałszywego administratora/klucza HTTP. Dotychczasowe operacje pozostają niezmienne.
- Prywatne widoki `dynamic_promotion_targets` i `active_promotion_claims` wyznaczają aktualną kwalifikację i konflikty także dla SQL. Nowe tabele mają RLS i brak uprawnień Data API; widoki security_invoker i odebrany dostęp publicznym rolom.
- Migracja `AddDynamicPromotions` jest addytywna wobec danych cenowych. Down odmawia przy nowych kampaniach lub ich operacjach; nie usuwa historii, aby wymusić cofnięcie.

## Transakcje i ponowienia

Wszystkie operacje zachowują kolejność CatalogWriteLock przed blokadami wierszy. `DynamicPromotionEngine` pracuje w transakcji właściciela komendy:

1. Sprawdza przecięcia aktualnych zakresów nowych kampanii i utrwalonych aktywnych udziałów starych kampanii.
2. Kończy okresy udziału wariantów opuszczających zakres/wykluczonych; dopisuje cenę podstawową z kontrolą aktualnego powiązania obniżki.
3. Ocenia nowych kandydatów. Poprawnym zapisuje udział, odniesienie i cenę; pozostałym zapisuje konflikty.
4. Rozwiązuje nieaktualne konflikty i zwiększa wersję kampanii po zmianie zastosowanych udziałów.

Najpierw przetwarzamy wyjścia wszystkich kampanii, potem wejścia. Przeniesienie między rozłącznymi zakresami nie zostawia dwóch aktywnych udziałów. Awaria SQL wycofuje całą transakcję; częściowa kwalifikacja biznesowa nie oznacza tolerowania częściowo zapisanego grafu.

Hooki komend katalogu, cen oraz promocji wykonują uzgodnienie przed commit, więc odczyt po udanym zapisie API widzi przeliczoną cenę. Konflikt nakładania wycofuje operację; jego diagnostykę zapisujemy osobną krótką transakcją. Nie rezerwuje to klucza nieudanej komendy.

Triggery PostgreSQL oznaczają konieczność uzgodnienia po zmianach kwalifikacji/cen. Odroczone ograniczenia odrzucają nakładanie także przy bezpośrednim SQL. Zmiana ceny/oferty wykonana poza API wymaga uzgodnienia przez worker: nie ma gwarancji natychmiastowego przeliczenia przed jego przebiegiem.

`PromotionReconciliationWorker` działa w API co 5 sekund, tworzy nowy scope DbContext, sprawdza trwały znacznik i ponownie stan pod blokadą. Po uzgodnieniu pełna kontrola jest ponawiana po minucie również dla upływu czasu okna wcześniejszej ceny/przyszłego wpisu ceny. Awaria nie kasuje znacznika; restart lub drugi worker nie stosuje drugi raz rabatu. Nie potrzebujemy nowej usługi zewnętrznej.

## Zgodność

- Produkt i kolekcja zachowują zamrożony skład oraz dotychczasowe hashe i zapisane odpowiedzi komend.
- Nowe zakresy mają własny fingerprint `dynamic/v1:`.
- Podgląd nowych zakresów zwraca `scopeToken`, obejmujący wersję kampanii i uporządkowany wynik kwalifikacji/cen. Aktywacja porównuje token ze świeżą oceną. Token nie jest poświadczeniem autoryzacji; nie zawiera sekretu.
- Nowe zakresy nie przesyłają pełnej listy wersji cen w body. Stare aktywacje nadal wymagają dotychczasowych pól.
- Szczegóły/lista nowego zakresu mają licznik `activeVariants`, pustą zagnieżdżoną tablicę `variants` i osobny odczyt stronicowany. Zapobiega to rozbudowaniu odpowiedzi o cały sklep.
- Zwykła zmiana podstawy aktywnego udziału nadal jest blokowana. Regułę aktywnej kampanii zmieniamy przez zakończenie i utworzenie nowej. Wykluczenia mają osobną wersjonowaną komendę.

## Kontrakt API

Prefiks `/api/v1/web/admin/promotions`. Dotychczasowa sesja administratora, no-store; zapis dodatkowo X-Requested-With: XMLHttpRequest i UUID Idempotency-Key.

| Metoda i trasa | Zachowanie |
| --- | --- |
| POST /, PUT /{id} | Szkic: scope catalog/category, dla category categoryId, name, discountType, discountValue, excludedProductIds, excludedVariantIds; edycja ma expectedVersion. |
| POST /{id}/preview?page=1&pageSize=20 | canActivate, scopeToken, totalVariants, eligibleVariants, conflictedVariants, stronicowane items z błędami. Brak poprawnych rabatów nie blokuje startu, ale pusty zakres i nakładanie blokują. |
| POST /{id}/activate | expectedVersion i expectedScopeToken dla nowych zakresów. |
| POST /{id}/end | expectedVersion; działa także przy zerowej liczbie aktywnych udziałów. |
| GET /{id}/exclusions | Wykluczenia i aktualna wersja. |
| PUT /{id}/exclusions | expectedVersion, productIds, variantIds; zastępuje cały zestaw, przelicza aktywną kampanię. |
| GET /conflicts | promotionId opcjonalne, includeResolved domyślnie false, page/pageSize; bezpośrednie wsparcie przyszłego panelu. |
| GET /{id}/variants | Stronicowane okresy udziału wraz z endedAt i wcześniejszym odniesieniem. |
| GET / | Dotychczasowe filtry plus scope catalog/category i categoryId. |

Przykład szkicu: `{"scope":"category","categoryId":"UUID","name":"Kategoria -20 zł","discountType":"amount","discountValue":20,"excludedProductIds":[],"excludedVariantIds":[]}`.

Po podglądzie aktywacja: `{"expectedVersion":1,"expectedScopeToken":"TOKEN_Z_PODGLADU"}`. Wersję po każdej zmianie odczytać z odpowiedzi; automatyczne dołączenie/wyjście też może ją zwiększyć.

## Seeder i odbiór

Seeder dodaje dwa szkice: katalog procentowy i kategorię kwotową, z przykładowym wykluczeniem produktu/wariantu. Nie aktywuje ich automatycznie: mieszany katalog celowo zawiera konflikty. `created` otrzymuje `promotionExclusions` oraz `promotionConflicts` (0 do faktycznej oceny); `added` pozostaje sumą liczników. Wewnętrzny znacznik uzgodnienia nie jest rekordem demonstracyjnym i nie ma licznika.

Reset obejmuje jawnie 3 nowe tabele (29 łącznie z sześcioma pustymi tabelami zakupowymi), bez CASCADE. Odmowa przy jakimkolwiek zamówieniu pozostaje. Nie fabrykujemy systemowych wykonań ani konfliktów w seedzie. Ochrona historii i granice środowiska są w [seeder-maintenance.md](seeder-maintenance.md).

Etapy wykonania:

- [x] DP1 — model, migracja, ograniczenia PostgreSQL, okresy udziału.
- [x] DP2 — zakresy katalog/kategoria, wykluczenia, podgląd/token i komendy.
- [x] DP3 — dynamiczne wejście/wyjście, częściowa kwalifikacja i konflikty.
- [x] DP4 — integracja zapisu katalogu/cen, trwałe ponowienia i worker.
- [x] DP5 — API administracyjne i zgodność starych operacji.
- [x] DP6 — seeder, migracja z danymi, testy PostgreSQL/HTTP i pomiar większego katalogu.

Odbiór: 189/189 testów bez pominięć, build Release 0 błędów i ostrzeżeń. Skrypt tests/promotions/verify-local.ps1 potwierdził migrację od zera, aktualizację z istniejącą ceną, identyczny skrót wszystkich pól historii po Up i Down/Up, odmowę Down przy nowej kampanii i zgodność modelu EF. Raport: .local/test-results/dynamic-promotions-20261102-133200-4d8d6c53/dynamic.trx. Szczegóły w [testing.md](testing.md).

Poza tym zakresem: frontend panelu, harmonogramy startu/końca, sumowanie rabatów, automatyczny wybór korzystniejszej promocji, kupony i rabaty koszykowe, zmiana historycznych kwot oraz wdrożenie migracji na zdalnym Supabase.

Przed planowaniem i implementacją użyto protected-area-check; lista obszarów chronionych była pusta. Wzorzec odroczonych triggerów opiera się na [dokumentacji PostgreSQL 17](https://www.postgresql.org/docs/17/trigger-definition.html). Odbiór dotyczy kodu i lokalnej bazy, nie konfiguracji środowiskowej ani frontendu.

Pomiar na 5000 produktach / 15 000 wariantów: 14 993 zastosowane rabaty, 4 konflikty, start 2,6–3 s, koniec 1,1–1,3 s; inne zapisy czekają na wspólną blokadę. Trzy lokalne próbki nie stanowią p95 ani prognozy Supabase. [Wyniki i ograniczenia](catalog-performance-audit.md#dynamiczne-promocje-katalogukategorii--02112026). Zapis nowych okresów i cen jest zbiorczy, parametryzowany i atomowy.
