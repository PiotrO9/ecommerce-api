# Promocje produktu i wcześniejsza cena

Cel: opisać wdrożony przepływ promocji jednego produktu oraz zachować listę wykonanych prac i granice pierwszej wersji.

Status na 31.10.2026: pierwsza wersja wdrożona w kodzie i skonfigurowanej bazie Supabase. Realizacja planu na polecenie właściciela projektu. Kontrakt i przykłady Postmana są w [api-contract.md](api-contract.md#promocje-produktu). Szerszy kierunek: [project-direction.md](project-direction.md), model cen: [pricing-model.md](pricing-model.md).

## Zakres i przyjęte reguły

Jedna promocja obejmuje jeden produkt. Przy aktywacji bierzemy jego warianty `Published`; produkt musi mieć `Published` i kategorię. Każdy objęty wariant musi mieć obowiązującą cenę PLN, poprawną historię oferty i nie może mieć przyszłej ceny ani innej aktywnej promocji. Błąd jednego wariantu blokuje całą operację. Podgląd opisuje problemy, lecz nie rezerwuje składu ani cen.

Skład jest utrwalany podczas aktywacji. Później opublikowany wariant nie dołącza automatycznie. Cykl: `Draft → Active → Ended`; edytowalny jest tylko szkic, zakończonej akcji nie reaktywujemy. Zwykła zmiana ceny jest blokowana podczas aktywnego udziału. Koniec dopisuje cenę sprzedaży równą podstawowej także dla wariantów ukrytych w trakcie promocji.

- PLN, jedna kwota konsumencka; brak kalkulatora VAT/netto i obsługi wielu walut.
- `decimal`/`numeric(18,2)`, rabat `percentage` większy od 0 do 100 włącznie albo dodatni `amount` nieprzekraczający podstawy żadnego wariantu. Wartość reguły ma najwyżej dwa miejsca po przecinku.
- Końcowa cena jest zaokrąglana jeden raz do grosza przez `MidpointRounding.AwayFromZero`; np. 50% z 0,05 PLN daje 0,03 PLN. Wynik zero jest dopuszczony zgodnie z dotychczasowym modelem; nie oznacza to gotowej obsługi darmowego zamówienia.
- Aktywacja wymaga ceny niższej od wcześniejszej ceny odniesienia. To reguła pierwszej wersji produktu, a nie twierdzenie o zakazie wszystkich innych konstrukcji prawnych obniżek.
- Algorytm dotyczy zwykłych towarów. Kolekcje, cały katalog, kupony, harmonogramy, zmiana podstawy w trakcie akcji oraz wyjątki dla towarów szybko psujących się pozostają rozszerzeniami.

## Oferta i odniesienie

`variant_offer_events` rejestruje widoczność wynikającą ze statusów produktu/wariantu i przypisania kategorii. Triggery zapisują zdarzenia w tej samej transakcji również przy bezpośrednim SQL. Oś widoczności przecinamy z osią cen PLN; samo `PublishedAt` nie odtwarza przerw ani początku obowiązywania przyszłej ceny. Brak stanu magazynu nie zmienia widoczności katalogowej.

Okno kończy się bezpośrednio przed aktywacją i zaczyna 30 dni kalendarzowych wcześniej, o tej samej lokalnej godzinie w `Europe/Warsaw`; zdarzenia są w UTC. Uwzględniamy cenę obowiązującą na początku okna i kolejne ceny, także wcześniejsze promocje; pomijamy okresy ukrycia. Na niejednoznacznej godzinie DST wybieramy wcześniejszy moment UTC. Dla nieistniejącej godziny cofamy lokalny czas minutami do ostatniej poprawnej minuty, zachowując sekundy. To jawna reguła techniczna granicy okresu.

Nowy wariant ma kompletną historię od utworzenia; znana oferta krótsza niż 30 dni otrzymuje `SinceFirstOffer`. Pełne okno daje `Full30Days`. Migracja zapisuje istniejącym wariantom stan dopiero od chwili wdrożenia, z `source=import`, `complete_from_birth=false`. Nie cofa początku do starego `PublishedAt`. Przy nieznanej przeszłości aktywacja czeka na wiarygodne całe okno lub osobny potwierdzony import. Nie istnieje endpoint ręcznego fabrykowania historii oferty.

`PriceReduction` utrwala minimum, początek okresu, początek obniżki i podstawę ustalenia. Publiczny odczyt używa tego zapisu przez całą tę samą obniżkę, także po upływie kolejnych 30 dni. `savingPercent` liczymy względem wcześniejszej ceny; nie musi być równy procentowi reguły liczonej od podstawy. Stare rabaty bez powiązania mają `reduction=null` i nie są przedstawiane jako zweryfikowana promocja.

## Model i transakcje

| Tabela `app` | Rola |
| --- | --- |
| `promotions` | Produkt FK, nazwa, rodzaj/wartość rabatu, status, wersja, autor, daty |
| `promotion_variants` | Zamrożony zakres i daty udziału; unikalny aktywny udział per wariant |
| `price_reductions` | Niezmienny zapis wcześniejszej ceny i zakresu odniesienia |
| `variant_offer_events` | Niezmienne zdarzenia widoczności, granica kompletności, źródło |
| `promotion_operations` | Autor/klucz, skrót żądania i oryginalny wynik komendy |
| `variant_price_changes` | Nowe opcjonalne FK do operacji promocji i obniżki; wcześniejsze kwoty bez zmian |

Migracja `20261031120821_AddProductPromotions` zawiera FK z `RESTRICT`, CHECK, indeks aktywnego udziału, RLS i odebranie dostępu rolom Data API. Nie modyfikuje Supabase Auth. `UPDATE`/`DELETE` historii oferty, obniżek i operacji są blokowane; historia cen zachowuje swoją dotychczasową ochronę.

Wspólna transakcyjna blokada `pg_advisory_xact_lock(20261031,3)` jest pobierana **przed** blokadami operacji i wierszy. Triggery `BEFORE STATEMENT` stosują ją również przy zapisie produktów, wariantów, powiązań kategorii, cen, promocji i udziałów przez SQL. Seeder pobiera ją przed `TRUNCATE`. Zastępuje proponowany wcześniej układ wielu blokad produktu/UUID: eliminuje odwrotną kolejność blokad z istniejącymi triggerami katalogu. Serializuje zapisy katalogowe, nie blokuje zwykłych odczytów; przy większej liczbie administratorów wymaga ponownego pomiaru.

Auth jest sprawdzany poza transakcją. Aktywacja kontroluje wersję promocji, dokładny zestaw wariantów i wszystkie wersje cen z podglądu, następnie atomowo zapisuje status, udziały, obniżki, ceny i wynik. Zakończenie również stanowi jedną transakcję. Błędy wycofują wszystko. Nieudana operacja nie rezerwuje klucza.

UUID `Idempotency-Key` jest unikalny dla autora w przestrzeni komend promocji, odrębnej od zwykłego zapisu ceny. Ceny pochodne wskazują jedną nadrzędną operację; nie powielają jej klucza w starym indeksie cen. Powtórzenie zwraca oryginalny wynik z `replayed=true`, nawet po późniejszym zakończeniu akcji; inna treść pod tym kluczem daje `409`. Dla ponowienia utworzenia szkicu zwracamy `200`, dla pierwszego utworzenia `201`.

## Lista realizacji

- [x] Reguły rabatów, granice czasu, krótka/nieznana historia i zakres pierwszej wersji.
- [x] Rejestr oferty z triggerami i stanem początkowym bez wymyślania przeszłości.
- [x] Encje, mapowania, migracja, ograniczenia, uprawnienia i ochrona historii.
- [x] Kalkulatory kwot i wcześniejszej ceny oraz utrwalony punkt odniesienia.
- [x] Tworzenie/edycja szkicu, podgląd, aktywacja i zakończenie w `PromotionService`.
- [x] Wspólna kolejność blokad z serwisem zwykłej ceny i seederem.
- [x] Siedem tras administratora, walidacja, CSRF, ponowienia i OpenAPI.
- [x] Spójne dane ceny w szczegółach, katalogu, kategorii i wyszukiwaniu; zachowane `amount` i `currency`.
- [x] Jeden reprezentatywny wariant ceny kafelka; filtry i sortowanie używają ceny sprzedaży.
- [x] Seeder: 14 tabel, pięć nowych liczników i scenariusze aktywnej/zakończonej promocji oraz krótkiej/nieznanej historii.
- [x] Testy PostgreSQL: równoczesne aktywacje i zwykła cena, kontrola wersji/składu, powtórzenia, rollback po rzeczywistym błędzie zapisu, zakończenie po ukryciu, zamrożony skład.
- [x] Testy kalkulatorów, dostępu i DTO; zachowanie odniesienia po 31 dniach oraz wszystkich publicznych odczytów.
- [x] Próba migracji z danymi: 43 467 cen, identyczna liczba i MD5 ID/kwot/dat przed/po; istniejące 16 082 warianty mają niepełny stan początkowy.
- [x] Pomiar zapisu 56 wariantów i odczytu HTTP na 5000 produktów; szczegóły w [audycie katalogu](catalog-performance-audit.md#promocje-produktu--31102026).
- [x] Migracja zastosowana lokalnie i w skonfigurowanym Supabase; build, 116 testów bez pominięć, zgodność modelu EF z migracją.
- [x] Odbiór całego przepływu przez HTTPS z dwoma prawdziwymi kontami Supabase (01.11.2026): 23 sprawdzenia w `tests/smoke/promotions.ps1`. Zamiast ręcznych kliknięć wykonano rzeczywiste żądania HTTP, logowanie i cookie; katalog oraz uprawnienia administratora były w izolowanym lokalnym PostgreSQL, Auth w skonfigurowanym Supabase. Nie jest to test połączenia katalogu z bazą Supabase ani odbiór frontendu. Konta tymczasowe usunięto i sprawdzono ich brak. Szczegóły w [testing.md](testing.md#odbiór-promocji-przez-https--01112026).

## Źródła i dalsze rozszerzenia

Źródła zakresu informacji o wcześniejszej cenie, krótkiej ofercie i towarach szybko psujących się: [UOKiK](https://prawakonsumenta.uokik.gov.pl/prawo-do-informacji/informacje-o-obnizkach-cen/). Spójność wariantów i niezmienność odniesienia przy niezmienionej obniżce: [komunikat UOKiK dotyczący Zalando i Temu](https://uokik.gov.pl/obnizka-jaka-jest-kazdy-widzi-niekoniecznie-najnizsza-cena-z-30-dni-przed-obnizka-kary-dla-zalando-i-temu). Zweryfikowane 31.10.2026; powyższe reguły techniczne nie stanowią obsługi wszystkich wyjątków prawnych.

Następne osobne zadania: kolekcje i zakres całego katalogu, harmonogramy, zmiana podstawy w aktywnej akcji, zasady łączenia promocji oraz cena utrwalana w koszyku/zamówieniu. Przed planowaniem i wdrożeniem sprawdzono `protected-area-check`; lista obszarów chronionych jest pusta.
