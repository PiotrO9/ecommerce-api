# Koszyk i ulubione — wdrożony backend i plan odbioru

Cel: opisać wdrożony backend modułów 4.1–4.3 z [planu backendu](project-direction.md), uzgodnione reguły i pozostały odbiór. Dokument powstał na prośbę właściciela projektu 01.11.2026.

Rozszerzenie magazynu 01.11.2026: sam koszyk nadal nie rezerwuje. Wariant w koszyku/ulubionych zwraca także `quantityAvailable`, a status dostępności i `canCheckout` odejmują aktywne niewygasłe rezerwacje. Minimalne `POST /orders` tworzy rezerwację z całego koszyka konta i opróżnia go w tej samej transakcji; błąd zachowuje koszyk, ponowienie nie tworzy drugiego zamówienia. Wymaga również migracji `AddInventoryReservations`, niezastosowanej w zdalnym Supabase. Dawne wyniki KU opisują stan sprzed tego rozszerzenia; bieżące reguły i testy są w [magazynie](inventory-model.md) oraz [testing.md](testing.md).

**Status 01.11.2026: backend KU1–KU4 wdrożony i sprawdzony automatycznie na PostgreSQL 17; dodatkowo 54/54 sprawdzenia przez rzeczywiste HTTPS i Supabase Auth, bez atrapy Auth.** Koszyk gościa pozostaje osobno, po logowaniu widoczny jest wyłącznie koszyk konta. Ulubione konkretnych wariantów są dostępne tylko na koncie. Migracja `20261101132248_AddShoppingCartsAndFavorites` została zastosowana wyłącznie lokalnie; nie zastosowano jej w Supabase. KU0 i KU5 pozostają częściowo otwarte: adresy środowisk, frontend oraz odbiór zdalnej bazy po migracji. Szczegóły wyników są w [testing.md](testing.md#koszyk--odbiór-https-z-rzeczywistym-auth-01112026).

## 1. Analiza stanu wyjściowego

Poniższa analiza opisuje punkt startowy przed implementacją koszyka i ulubionych, a nie brak tych funkcji w aktualnym kodzie.

| Obszar | Stan potwierdzony w kodzie | Wniosek dla koszyka i ulubionych |
| --- | --- | --- |
| Tożsamość | `AuthenticatedUserService.GetAsync` sprawdza użytkownika w Supabase Auth i obsługuje odświeżanie sesji; rozróżnia brak uwierzytelnienia i niedostępność usługi. | Właściciela danych konta należy ustalać przez ten serwis, bez `userId` w żądaniu. Awaria Auth nie może przełączyć operacji na gościa. |
| Logowanie i rejestracja | `AuthController.Login` wystawia ciasteczko sesji. `Register` zwraca `201`, bez ustanowienia sesji. | Dopiero zweryfikowana sesja przełącza widok na koszyk i ulubione konta. Sama rejestracja nie zmienia właściciela; nie przenosimy danych gościa. |
| Ciasteczko | `AuthCookieService` używa `__Host-ecomm-auth`, Data Protection, `HttpOnly`, `Secure`, `SameSite=Lax` i `Path=/`. | Gość potrzebuje osobnego mechanizmu. Nie należy wkładać koszyka do ciasteczka uwierzytelnienia. |
| Profil | `UserProfile` powstaje przy pierwszym zapisie profilu; konto może istnieć bez niego. | Koszyk konta nie może wymagać wypełnionego profilu ani FK do opcjonalnego `user_profiles`. |
| Katalog | `Product` ma warianty; SKU, cechy, zdjęcia, cena i stan dotyczą `ProductVariant`. Produkt i wariant mają własny status. | Pozycja koszyka musi wskazywać wariant. Zgodnie z decyzją użytkownika ulubione również wskazują konkretny wariant. |
| Ceny i promocje | Katalog wybiera cenę z ostatniego obowiązującego wpisu historii; `PublicPriceBuilder` buduje publiczne dane ceny i wcześniejszego odniesienia. Promocje produktu i kolekcji zapisują wynik w historii. | Koszyk powinien odczytywać tę samą cenę sprzedaży, bez własnego kalkulatora rabatów i bez ponownego odejmowania promocji. |
| Stan magazynowy | `VariantStock` przechowuje `QuantityOnHand`; brak rekordu daje nieznany stan. Nie ma rezerwacji ani checkoutu. | Koszyk może informować o dostępności, ale nie gwarantuje towaru. Brak rekordu musi blokować przyszły zakup, zgodnie z istniejącym ustaleniem. |
| Dostęp do bazy | EF Core używa prywatnego schematu `app`; kontekst HTTP nie staje się automatycznie kontekstem RLS. | Każde zapytanie o koszyk lub ulubione musi być ograniczone do właściciela ustalonego przez backend. |
| Ochrona zapisów | Profil i operacje administracyjne sprawdzają `X-Requested-With: XMLHttpRequest`. `Program.cs` nie konfiguruje CORS. | Nowe zapisy wymagają analogicznej ochrony; przed połączeniem z frontendem trzeba uzgodnić hostowanie i zaufane origins. |
| Seeder | `DemoCatalogSeeder` wykonuje jawny `TRUNCATE ... RESTRICT` siedemnastu tabel katalogowych. | Nowe FK pozycji koszyka i ulubionych do wariantów wpłyną na reset, nawet jeśli tabele pozycji będą puste. Zmianę seedera trzeba zaplanować razem z migracją. |

Przed implementacją sprawdzono `Controllers/`, `Entities/`, `Data/AppDbContext.cs` i serwisy: nie było encji, serwisów ani endpointów koszyka i ulubionych. Aktualne działanie opisują dalsze części oraz [kontrakt API](api-contract.md#koszyk-i-ulubione--01112026).

Źródła lokalne: [architektura](architecture.md), [profil](user-profiles.md), [ceny](pricing-model.md), [magazyn](inventory-model.md), [testy](testing.md), [utrzymanie seedera](seeder-maintenance.md) oraz wymienione pliki kodu. Analiza dotyczy aktualnego drzewa roboczego, także obecnych w nim zmian kolekcji; nie jest ponownym odbiorem środowiska Supabase.

## 2. Zakres pierwszej wersji i zależności

Uzgodniony rezultat: gość może utrwalać koszyk, a zalogowany klient koszyk i ulubione. Po logowaniu widzi wyłącznie dane konta. Koszyk gościa pozostaje osobno; nie jest kopiowany ani scalany z kontem. Ulubione nigdy nie powstają dla gościa.

- Koszyk: wybrany wariant, dodatnia całkowita ilość, ustawienie ilości, usunięcie pozycji, wyczyszczenie i bieżące podsumowanie PLN.
- Ulubione: wyłącznie dla zalogowanego klienta; lista konkretnych wariantów, dodanie, usunięcie i stronicowany odczyt. Różne warianty tego samego produktu są osobnymi wpisami. Gość nie ma ulubionych.
- Jedno konto ma jeden aktywny koszyk i jedną listę ulubionych, wspólne dla urządzeń. Gość ma wyłącznie koszyk w ramach danej przeglądarki.
- Pierwsza wersja nie rezerwuje ani nie pomniejsza magazynu. Checkout, zamówienie, dostawa, podatki i płatność należą do kolejnych modułów.
- Kupony, promocje zależne od wartości koszyka, wiele list ulubionych, udostępnianie koszyka i Redis są poza proponowanym zakresem.
- Logowanie Google (4.4) pozostaje osobnym etapem uwierzytelniania. Wybór koszyka konta i listy ulubionych zależy od zweryfikowanego UUID, niezależnie od przyszłej metody logowania.

Model i testy backendu można przygotować bez gotowego frontendu i bez checkoutu. Odbiór przeglądarkowy wymaga ustalenia adresów frontendu/API, cookies i CORS. Istniejący katalog developerski wystarczy do testów; brak administracyjnego tworzenia produktów nie blokuje tej pracy.

## 3. Wybór przechowywania i identyfikacji

| Rozważany wariant | Zalety | Koszt lub ograniczenie |
| --- | --- | --- |
| Gość w `localStorage`, konto w PostgreSQL | Mało zapisów serwerowych dla gościa. | Dwa modele zachowania i brak wspólnej obsługi wersji oraz cen po stronie API. |
| Koszyk gościa i dane konta w PostgreSQL; token gościa w ciasteczku | Wspólne reguły koszyka i łatwiejsze testy ponowień; ulubione są osobnym zasobem konta. | Trzeba ograniczać tworzenie anonimowych danych i ustalić ich wygasanie. |

**Uzgodnione: PostgreSQL dla obu rodzajów właścicieli i osobny token gościa w ciasteczku** (decyzja użytkownika z 01.11.2026). Pasuje do istniejącej architektury EF Core i zapewnia wspólne reguły obsługi obu zakresów. Nie wymaga anonimowych kont Supabase Auth. Gość i konto mają oddzielne koszyki; konfiguracja adresów frontendu/API pozostaje do ustalenia.

Zasady wdrożone w backendzie i wymagania dla przyszłego frontendu:

1. Gość dostaje losowy token o entropii 256 bitów w osobnym ciasteczku `__Host-ecomm-shopping`, `HttpOnly; Secure; SameSite=Lax; Path=/`, bez `Domain`. Baza przechowuje tylko jego skrót; token nie trafia do URL, JSON ani logów.
2. Pierwszy zapis gościa poprzedza `POST /api/v1/web/shopping/guest-session`. Sam odczyt pustego koszyka nie tworzy rekordu. Frontend inicjuje sesję jednokrotnie przed równoległymi zapisami; należy uwzględnić także równoczesne otwarcie dwóch kart.
3. Uzgodniony termin gościa: 30 dni od utworzenia, bez automatycznego przedłużania. Dane konta nie mają tego terminu i pozostają po wylogowaniu. Wygasanie sprawdza backend niezależnie od terminu ciasteczka; fizyczne usuwanie danych wymaga osobnego mechanizmu i reguł retencji.
4. Konto jest rozpoznawane wyłącznie przez `AuthenticatedUserService`. Identyfikator właściciela nie jest parametrem publicznego API.
5. Gdy ciasteczka Auth nie ma, można obsłużyć gościa. Gdy jest, ale sesja jest nieważna, API zwraca `401`; gdy Auth jest niedostępne, zwraca `503`. Nie zapisuje wtedy danych w innym zakresie. Usunięcie nieważnej sesji i ewentualna inicjalizacja gościa są jawnym kolejnym krokiem frontendu.
6. Posiadanie obu ciasteczek nie łączy danych. Przy ważnej sesji endpointy koszyka obsługują wyłącznie konto; token gościa pozostaje zachowany, ale nie wybiera aktywnego koszyka. Ulubione nie korzystają z tokenu gościa.
7. Po wylogowaniu frontend usuwa dane konta z pamięci i ponownie odczytuje koszyk gościa. Wcześniejszy koszyk gościa jest dostępny, jeśli token i dane nie wygasły; w przeciwnym razie widoczny jest pusty koszyk. Nie kopiujemy danych konta do gościa. Wylogowanie nie usuwa ciasteczka koszyka gościa; jawne porzucenie jego sesji jest osobną operacją.

Skopiowany token gościa daje dostęp do jego wyboru; jest poświadczeniem, nie publicznym ID. Utrata ciasteczka oznacza utratę dostępu gościa, bez odzyskiwania po e-mailu.

## 4. Wdrożony model danych

Migracja tworzy cztery tabele aplikacji w `app`; `auth.users` nadal pozostaje wyłączona z migracji EF. Nowe tabele nie zostały jeszcze utworzone w zdalnym Supabase.

| Tabela w `app` | Najważniejsze dane i ograniczenia |
| --- | --- |
| `shopping_states` | UUID `id`; opcjonalne `user_id` FK do `auth.users.id`; opcjonalny unikalny `guest_token_hash`; `expires_at` dla gościa; `cart_version`; daty UTC. CHECK: dokładnie jeden właściciel — konto albo token gościa. Unikalny indeks dla niepustego `user_id`. Logowanie nie zmienia właściciela rekordu. |
| `cart_items` | Klucz `(shopping_state_id, variant_id)`; FK do stanu i wariantu; `quantity` z CHECK; daty UTC. Jeden wariant najwyżej raz w koszyku. |
| `favorite_lists` | `user_id` PK i FK do `auth.users.id`; losowy unikalny `scope_id`; `version`; daty UTC. Lista istnieje tylko dla konta i nie wymaga uzupełnienia profilu. `scope_id` jest warunkiem zapisu chroniącym przed żądaniem ze starego konta, nie poświadczeniem dostępu. |
| `favorite_items` | Klucz `(user_id, variant_id)`; FK do listy konta i wariantu; `created_at`. Jeden wariant najwyżej raz na liście; różne warianty tego samego produktu mogą być zapisane osobno. Nie ma właściciela anonimowego. |

Stan koszyka konta i lista ulubionych powstają przez atomowy upsert; nie wymagają rekordu profilu. Usunięcie konta usuwa jego koszyk, listę ulubionych i pozycje. Nie dodajemy tabel, pól, kluczy operacji ani znaczników przejęcia danych gościa.

Usunięcie stanu koszyka usuwa jego pozycje; usunięcie listy ulubionych usuwa jej wpisy. Fizyczne usunięcie wariantu usuwa powiązane pozycje, również gdy warianty są usuwane w ramach operacji całego produktu. Samo usunięcie produktu nadal podlega istniejącym ograniczeniom katalogu. Ukrycie oferty zachowuje pozycję jako niedostępną. Koszyk nie pełni roli historii zakupu — tę rolę będzie mieć zamówienie.

Uzgodnione limity: 100 różnych wariantów w koszyku, 99 sztuk jednej pozycji i 500 ulubionych wariantów (decyzja użytkownika z 01.11.2026). Kontrola limitu liczby pozycji wymaga transakcji i blokady stanu; sam CHECK ilości nie wystarczy.

Nowe tabele mają RLS i odebrane uprawnienia rolom Data API oraz `PUBLIC`, zgodnie z obecną granicą prywatnego `app`. API nadal samodzielnie kontroluje właściciela. Odwołanie do konta używa tylko PK `auth.users.id`, bez modyfikowania schematu `auth`.

## 5. Reguły koszyka, cen i dostępności

1. Dodawanie lub zwiększanie ilości dopuszcza tylko opublikowany produkt i wariant, z kategorią, obowiązującą ceną PLN i znanym dodatnim stanem wystarczającym w chwili sprawdzenia. Brak ceny, ukryta oferta i nieznane ID nie ujawniają danych administracyjnych.
2. Odczyt ponownie sprawdza ofertę. Późniejszy brak towaru, ukrycie, brak ceny albo ilość mniejsza od żądanej nie usuwają pozycji po cichu: odpowiedź zawiera stan niedostępności. Klient może ją usunąć lub zmniejszyć ilość; operacji porządkowych nie blokuje brak dostępności.
3. Przykładowe oznaczenia: `available`, `out_of_stock`, `stock_unknown`, `insufficient_stock`, `unavailable`. Dla ukrytej oferty zwracamy minimalny identyfikator znanej pozycji i powód ogólny, bez aktualnych opisów i zdjęć szkicu.
4. Zapisujemy wybór i ilość, nie zamrożoną ofertę cenową. Odczyt wybiera ostatni wpis PLN z `effective_at <= now`, następnie `created_at` i `id` malejąco, tak jak katalog. Promocja produktu lub kolekcji jest już uwzględniona w `selling_amount`.
5. Jeden odczyt używa wspólnego `now` i spójnego obrazu bazy. Cenę, stan, warianty i informacje o obniżce pobieramy zbiorczo, bez ładowania całej historii i bez zapytania per pozycja. Wspólne wybieranie ceny należy wydzielić lub wykorzystać ponownie; `PublicPriceBuilder` jest formatowaniem wyniku, nie serwisem pobierającym cenę.
6. Odpowiedź podaje `evaluatedAt`, `currency=PLN`, wersję wyboru, pozycje, aktualne `priceChangeId`, cenę jednostkową, wartość pozycji i powody blokady. Frontend może porównać cenę z poprzednim odczytem. Trwałe powiadomienia o zmianie ceny między urządzeniami nie są zakresem pierwszej wersji.
7. Podsumowanie obejmuje tylko pozycje aktualnie możliwe do zakupu. Zwracamy osobno liczbę pozycji zablokowanych i `canCheckout`; pusty koszyk lub dowolna blokada daje `false`. To informacja o wyborze, nie potwierdzenie rezerwacji ani gotowego checkoutu. Wartość nie zawiera dostawy i jest nazwana jako suma towarów.
8. Przyszły checkout musi ponownie sprawdzić cenę i stan oraz atomowo rezerwować towar według osobno ustalonego modelu. Stan koszyka i `canCheckout` nie zastępują tej kontroli.

Użytkownik 01.11.2026 zatwierdził blokowanie dodania niedostępnego towaru do koszyka oraz rekomendację podsumowania z punktu 7. Blokujemy także zwiększenie ilości ponad bieżący stan. Wcześniej dodaną, później niedostępną pozycję zachowujemy z ostrzeżeniem. W tej wersji nie wprowadzamy przedsprzedaży ani rezerwacji; przyszły checkout nadal wymaga osobnych ustaleń magazynowych.

## 6. Reguły ulubionych

- Uzgodnione: ulubione są dostępne **wyłącznie dla zalogowanego klienta**. Brak sesji daje `401` dla odczytu i zapisu; nie tworzymy listy gościa ani importu ulubionych. Frontend prowadzi gościa do logowania zamiast potwierdzać zapis.
- Uzgodnione: zapamiętujemy **konkretny wariant**, bez ilości. Przykład: czerwona koszulka M i czerwona koszulka L są dwoma ulubionymi. Dodanie do koszyka używa zapisanego `variantId` i przechodzi aktualną walidację koszyka, bez ponownego wybierania wariantu.
- Nowy wpis wymaga publicznie widocznego produktu i wariantu z obowiązującą ceną PLN. Brak towaru nie blokuje zapisania wariantu do ulubionych; zatwierdzona blokada dodawania niedostępnego towaru dotyczy koszyka.
- Ponowne dodanie nie tworzy duplikatu; usunięcie nieobecnego wpisu ma ten sam skutek co usunięcie istniejącego.
- Ukryty produkt lub wariant pozostaje jako minimalna pozycja niedostępna, możliwa do usunięcia, bez ujawniania aktualnych danych szkicu. Fizyczne usunięcie wariantu usuwa wpis, również przy usunięciu całego produktu.
- Odczyt jest stronicowany, roboczo 20 elementów, maksymalnie 50; kolejność `created_at DESC, variant_id` jest stabilna dla niezmienionej listy. Odpowiedź zawiera wersję całej listy, aby frontend wykrył zmianę między stronami.
- Kafelek przedstawia zapisany wariant: jego SKU, cechy, zdjęcie i cenę, z nazwą i slugiem produktu. Korzysta ze wspólnych reguł widoczności i odczytu ceny; nie pokazuje najtańszego innego wariantu jako ceny zapisanej pozycji.

## 7. Rozdzielenie koszyków i współbieżność

Uzgodnione 01.11.2026: **koszyk gościa pozostaje osobno; po zalogowaniu widoczny jest wyłącznie koszyk konta**. Nie pytamy o import, nie scalamy ilości i nie dodajemy operacji przenoszenia. Reguła braku duplikatów dotyczy wpisów tego samego wariantu w jednym koszyku lub jednej liście ulubionych, a nie łączenia właścicieli.

Przykład: gość ma wariant A, konto ma wariant B. Po logowaniu widoczny jest tylko B. Po wylogowaniu znów widoczny jest A, jeśli sesja gościa nie wygasła. Konto nie mające koszyka zaczyna od pustego wyboru, nawet gdy koszyk gościa zawiera pozycje. Konto B nie otrzymuje wyboru konta A ani koszyka gościa.

Logowanie i wylogowanie zmieniają aktywną tożsamość, bez przepisywania pozycji w bazie. Frontend po zmianie sesji usuwa dotychczasowe dane z widoku, odczytuje nowy zakres i ignoruje spóźnione odpowiedzi dla poprzedniego właściciela. Sesja gościa nie przedłuża się na czas pracy na koncie. Ulubione są pobierane tylko po potwierdzeniu sesji konta i znikają z pamięci frontendu po wylogowaniu.

Zwykłe zapisy koszyka i ulubionych używają odpowiednio `expectedVersion` oraz `expectedScopeId` otrzymanych przy odczycie; usunięcia przekazują je w query. Backend najpierw ustala właściciela z sesji, a następnie porównuje identyfikator jego koszyka/listy — parametr nie służy do wyboru cudzych danych. Sama wersja liczbowa nie wystarczy: koszyk gościa i konta A/B mogą mieć taką samą wersję. Niezgodny zakres daje `409` bez zapisu i chroni przed opóźnionym żądaniem z karty sprzed logowania lub zmiany konta.

PUT ustawia ilość docelową, nie przyrost. Stara wersja daje `409`, więc ponowione żądanie nie dodaje kolejnych sztuk. Po konflikcie klient odczytuje stan i porównuje zamiar użytkownika; nie powtarza bezwarunkowo starego zapisu ani nie podmienia zakresu na nowe konto. Wersja wyboru nie jest wersją ceny: zmiana promocji może zmienić podsumowanie bez edycji koszyka.

## 8. Wdrożone API

Poniższe trasy istnieją w kodzie, ze wspólnym prefiksem `/api/v1/web`. Wymagają zastosowania nowej migracji w używanej bazie. Endpointy koszyka wybierają właściciela z sesji, nie z parametrów. Endpointy ulubionych wymagają konta. Warunki zapisu `expectedVersion` i `expectedScopeId` chronią przed konfliktem i zmianą tożsamości; nie uprawniają do wskazanego zasobu.

| Metoda i trasa | Zachowanie |
| --- | --- |
| `POST /shopping/guest-session` | Inicjalizacja gościa przy braku sesji konta; ponowne użycie ważnego tokenu, limit tworzenia nowych rekordów. |
| `GET /cart` | Bieżący wybór, wersja i aktualna wycena; bez stanu gościa zwraca pusty koszyk bez tworzenia danych. |
| `PUT /cart/items/{variantId}` | Ustawienie `quantity` z `expectedVersion`; dodanie albo zmiana pozycji. |
| `DELETE /cart/items/{variantId}` | Usunięcie pozycji z kontrolą wersji. |
| `DELETE /cart` | Wyczyszczenie koszyka z kontrolą wersji. |
| `GET /favorites` | Wyłącznie konto: strona ulubionych z metadanymi paginacji i wersją listy; gość dostaje `401`. |
| `PUT /favorites/items/{variantId}` | Wyłącznie konto: dodanie konkretnego wariantu z kontrolą wersji. |
| `DELETE /favorites/items/{variantId}` | Wyłącznie konto: usunięcie konkretnego wariantu z kontrolą wersji. |
| `DELETE /shopping/guest-session` | Jawne porzucenie wyboru gościa i unieważnienie jego tokenu, także podczas pracy na koncie. |

Zapisy wymagają `X-Requested-With: XMLHttpRequest`, a body — `application/json`. Nie przyjmują `userId`, tokenu gościa, ceny, nazwy ani statusu produktu od klienta. Wszystkie odpowiedzi osobiste mają `Cache-Control: no-store`. Nie dodajemy endpointów importu ani scalania.

Błędy: `400` dla formatu/ilości, `401` dla nieważnej sesji konta lub braku konta przy operacji ulubionych, `403` dla braku wymaganego nagłówka, `404` dla niewidocznego lub nieznanego celu, `409` dla wersji, zmiany właściciela, dostępności lub limitów, `429` dla limitu żądań i `503` dla niedostępności Auth/bazy. Wygasły token gościa przy zapisie daje `409 guest_session_expired`; pusty odczyt może polecić ponowną inicjalizację. Odczyt niedostępnej pozycji zapisanej wcześniej zwraca placeholder zamiast `404` całej listy.

Błędy biznesowe mają `{ code, message }`. Walidacja modelu MVC, nieznane pola JSON i nieprawidłowy typ treści mogą zwrócić standardowe Problem Details (`400`/`415`); istniejące odpowiedzi innych modułów nie zostały przebudowane. Middleware ustawia `no-store` także dla odrzuceń przed kontrolerem. POST inicjalizacji gościa i DELETE nie wymagają body. PUT wymaga JSON; wynik poprawnego zapisu to `{ scopeId, version }`, następnie klient odczytuje aktualny widok.

Limity na adres IP: `Shopping:WriteRequestsPerMinute` domyślnie 120 dla zapisów, `Shopping:GuestSessionsPerHour` domyślnie 10 dla inicjalizacji gościa. Są lokalne dla procesu API; produkcyjne progi i działanie za proxy wymagają odbioru środowiska. `429` nowych tras ma `no-store` i kod `shopping_rate_limited`.

## 9. Podział wdrożenia

KU0 jest częściowo wykonany: reguły produktu i kontrakt backendu zostały ustalone, pozostają adresy i integracja frontendu. KU1–KU4 są gotowe w kodzie i lokalnych testach. KU5 jest częściowy: automatyczny odbiór i 54 sprawdzenia z rzeczywistym Auth są wykonane, nie ma odbioru frontendowego ani zastosowania migracji w Supabase. Dwie niezależne sesje HTTP sprawdzono jako odpowiednik dwóch urządzeń, bez przeglądarkowego UI. Opisy kryteriów niżej nie stanowią potwierdzenia pozostałych zewnętrznych kroków.

| Etap | Zakres | Kryterium zakończenia |
| --- | --- | --- |
| KU0 — uzgodnienia | Decyzje produktu zapisane w części 11; do dokończenia kontrakt z frontendem, hostowanie/cookies i ponowna kontrola obszarów chronionych. | Zapisane reguły rozdzielenia koszyków, ulubionych konta, dostępności, limitów i czasu życia oraz warunki integracji. |
| KU1 — dane i właściciel | Encje, mapowanie, migracja EF, ograniczenia, RLS/uprawnienia, `ShoppingOwnerResolver`, `GuestShoppingSessionService`, dostosowanie resetu seedera. | Izolowane testy PostgreSQL i HTTP potwierdzają dostęp, wygasanie, równoczesne utworzenie stanu i bezpieczny ponowny seed. |
| KU2 — koszyk | `CartService`, wspólny odczyt ceny/oferty, DTO i `CartController`; guest i konto korzystają z tych samych reguł. | Dodawanie, ustawienie ilości, usuwanie, konflikty wersji i wycena działają bez rezerwacji oraz bez N+1. |
| KU3 — ulubione | `FavoritesService`, `FavoritesController`, lista wyłącznie konta, paginacja, dostępność i deduplikacja po `variantId`. | Konto zapisuje wariant raz; może zapisać dwa warianty jednego produktu, widzi ich własne ceny i może usuwać pozycje niedostępne. Każda operacja gościa kończy się `401`. |
| KU4 — przełączanie tożsamości | Integracja logowania/wylogowania bez przenoszenia danych, zachowanie koszyka gościa i odrzucanie zapisów ze starego zakresu. | Po logowaniu widać tylko koszyk konta; po wylogowaniu wraca niewygasły koszyk gościa. Ulubione są wyłącznie na koncie, a opóźniony zapis nie zmienia danych innego właściciela. |
| KU5 — odbiór | Pełny pipeline HTTP, dwa konta, dwie karty/urządzenia, integracja frontendu, testy zmian promocji i stanów, dokumentacja. | Działa ścieżka gość → koszyk → logowanie → właściwy koszyk konta i jego ulubione → zmiana na drugim urządzeniu → wylogowanie → inne konto bez dostępu do poprzednich danych. |

Kolejność: `KU0 → KU1 → KU2/KU3 → KU4 → KU5`. KU2 i KU3 mają wspólną zależność od KU1 i mogą być wykonywane niezależnie po ustaleniu kontraktów. Całość nie zamyka etapu 4.4 Google ani modułu checkoutu.

Implementacja: `Entities/ShoppingState.cs`, `Data/ShoppingModelConfiguration.cs`, `Data/AppDbContext.cs`, nowa migracja i `ShoppingRules.cs`, `Contracts/Shopping/ShoppingContracts.cs`, `Services/Shopping/`, kontrolery `CartController`, `FavoritesController`, `ShoppingController` i ich baza oraz rejestracje/middleware w `Program.cs`. `CurrentVariantPrices.Query` jest wspólne dla list katalogu i odczytu wyboru klienta. Istniejący logout nie usuwa ciasteczka gościa i nie wymagał zmiany kodu Auth. Seeder dostosowano do nowych FK i wersji.

## 10. Testy, seeder i odbiór

**Testy reguł i HTTP:** dodatnie i graniczne ilości, błędne/nieznane pola, brak ceny PLN, promocja produktu i kolekcji bez ponownego rabatu, zmiana ceny po dodaniu, stan `null`/`0`/mniejszy od ilości, ukryta oferta, usuwanie niedostępnej pozycji, paginacja i duplikaty. Dla ulubionych: dwa warianty tego samego produktu są osobnymi wpisami, ten sam wariant nie duplikuje się, cena i zdjęcie pochodzą z zapisanego wariantu, a dodanie do koszyka nie podmienia go na inny. Rzeczywiste żądania HTTP mają sprawdzić cookies, `no-store`, CSRF i serializację konfliktów; same wywołania metod kontrolera nie wystarczą.

**Testy izolacji i współbieżności na PostgreSQL:** konto A/B, gość A/B, brak profilu, cudzy identyfikator pozycji, awaria Auth bez fallbacku, dwie pierwsze inicjalizacje, dwa zapisy tej samej wersji, limity przy równoczesnych insertach, brak ulubionych gościa oraz odrzucanie zapisu ze starego zakresu po zmianie właściciela, także gdy numery wersji koszyków są równe. Scenariusze logowania: zachowanie koszyka gościa bez zmiany, puste nowe konto mimo pozycji gościa, powrót koszyka gościa po wylogowaniu, wygaśnięcie gościa podczas pracy na koncie i przełączenie A → B bez przeniesienia pozycji. Zweryfikować również FK, indeksy, uprawnienia Data API i usuwanie konta.

**Seeder — wykonane w KU1:**

- Obie tabele pozycji dodano do jawnego `TRUNCATE ... RESTRICT`: rozszerzenie koszyka dodało dwie tabele do resetu, bez `CASCADE`. Reset tego zakresu obejmuje 19 tabel; pełna lista jest w kodzie i `demo-seed.md`.
- Stany właścicieli, listy ulubionych, profile i konta pozostają; usuwane są pozycje odnoszące się do resetowanego katalogu. Przed resetem zwiększane są wersje dotkniętych koszyków i list.
- Reset pobiera wyłączną blokadę `(20261101,4)` po dotychczasowych blokadach katalogu/resetu. Odczyty i zapisy modułu pobierają współdzieloną postać tej blokady; nie serializuje ona różnych klientów, lecz wyklucza reset. Zapisy pozycji mają również trigger blokady dla SQL. Równoczesny reset i zapisy koszyka/ulubionych sprawdza test PostgreSQL.
- Wyjątek od tworzenia nowych danych demonstracyjnych: seeder katalogu nie tworzy koszyków ani ulubionych rzeczywistych klientów i nie zakłada kont Auth. Te scenariusze przygotowują izolowane fixture testowe lub ręczna ścieżka przez API z katalogiem demo. Powód: dane należą do osoby/przeglądarki, a nie do publicznego katalogu; tokenów dostępu nie publikujemy w odpowiedzi seedera.
- `created` i `added` nadal liczą faktycznie utworzone rekordy typów katalogowych; nie dodano fikcyjnych liczników koszyków, ulubionych ani usunięć.

Testy integracyjne wykorzystują izolowaną bazę `ecomm_test_*` na localhost i kolekcję `Postgres catalog`, zgodnie z obecnym układem. Nie uruchamiają resetu na danych sklepu w Supabase. Końcowy odbiór to `dotnet build E-commerce-backend.slnx`, odpowiednie `dotnet test E-commerce-backend.slnx` z PostgreSQL oraz próba HTTPS z frontendem; pominięte integracje nie oznaczają zaliczenia.

Aktualny kontrakt znajduje się w [api-contract.md](api-contract.md#koszyk-i-ulubione--01112026). Odbiór HTTPS z rzeczywistym Supabase Auth przeszedł: 54/54 sprawdzenia, dwa tymczasowe konta i osobne sesje. Konta wylogowano, usunięto i sprawdzono `404`; katalog i modyfikacje fixture były wyłącznie lokalne. Pozostaje przygotowanie odbioru przeglądarkowego w repozytorium frontendu, ustalenie domen i osobno uzgodnione zastosowanie migracji oraz odbiór zdalnej bazy. W sprawdzonym frontendzie są tylko strona główna i design system, bez ekranów i integracji koszyka/ulubionych. Fizyczne czyszczenie wygasłych koszyków gościa nadal nie jest wdrożone.

## 11. Decyzje użytkownika i sprawy otwarte

Źródło poniższych ustaleń: odpowiedź właściciela projektu na punkty 1–7 oraz doprecyzowania, że niezalogowany użytkownik nie ma ulubionych i koszyk gościa pozostaje osobno po logowaniu, 01.11.2026. Zatwierdzenie reguł nie jest wdrożeniem funkcji.

| Decyzja | Ustalenie lub propozycja | Status |
| --- | --- | --- |
| Gdzie przechowywać dane? | Koszyk gościa oraz koszyk i ulubione konta w PostgreSQL; osobny token gościa w ciasteczku. | Uzgodnione. |
| Co zapisują ulubione? | Konkretny wariant; różne warianty jednego produktu są osobnymi wpisami. | Uzgodnione. |
| Kto ma ulubione? | Wyłącznie zalogowany klient; gość nie ma listy ani importu ulubionych. | Uzgodnione. |
| Czy przenosić koszyk po logowaniu? | Nie. Koszyk gościa pozostaje osobno, po logowaniu widoczny jest wyłącznie koszyk konta. | Uzgodnione; nie tworzymy mechanizmu scalania ani importu. |
| Jak długo utrzymywać gościa? | 30 dni od utworzenia bez przedłużania; konto bez tego terminu. | Uzgodnione; fizyczna retencja wygasłych danych nadal otwarta. |
| Jakie limity? | 100 wariantów koszyka, 99 sztuk pozycji, 500 ulubionych wariantów. | Uzgodnione; progi limitów żądań i tworzenia sesji gościa pozostają technicznym ustaleniem. |
| Brak towaru przy dodawaniu do koszyka? | Blokada dodania/zwiększenia; wcześniej dodana pozycja pozostaje z ostrzeżeniem. | Uzgodnione. |
| Jak prezentować niepełny koszyk? | Suma tylko dostępnych pozycji, jawne blokady i `canCheckout=false`. | Uzgodnione zgodnie z rekomendacją. |
| Jak hostować frontend? | Ustalić adresy, następnie konfigurację cookies/CORS/CSRF. | Otwarta — zgoda na PostgreSQL i ciasteczko nie określa domen. |

## 12. Obszary chronione i źródła zewnętrzne

Kontrola skillem `.agents/skills/protected-area-check/SKILL.md` na 01.11.2026: lista w [protected-areas.md](protected-areas.md) jest pusta. Plan obejmuje przyszłe zmiany tożsamości gościa, wylogowania, dostępu do danych konta, schematu `app` i resetu seedera. Przed implementacją należy ponownie sprawdzić aktualną listę i rzeczywisty zakres; obecna kontrola nie zastępuje testów.

Oficjalne źródła sprawdzone przy analizie:

- [Supabase User Management](https://supabase.com/docs/guides/auth/managing-user-data): relacja danych aplikacji do PK `auth.users.id` i granica własności Auth.
- [Supabase Securing your API](https://supabase.com/docs/guides/api/securing-your-api): odrębne role grants, RLS i niewystawianych schematów. Plan zachowuje istniejący prywatny dostęp przez backend.
- [Changelog Supabase](https://supabase.com/changelog.md), w tym [zmiana szyfrów TLS](https://supabase.com/changelog/cbc-tls-ciphers-deprecated) i [zmiany PostgreSQL](https://supabase.com/changelog/postgres-15-19-17-11-breaking-changes): plan nie wprowadza zależności od opisanych tam rozszerzeń ani nowych API SDK. Nie jest to potwierdzenie konfiguracji TLS lub wersji zdalnej bazy.
