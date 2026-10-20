# Plan prac i postęp backendu sklepu

Cel: mieć w jednym miejscu kolejność modułów, listę prac i widoczny postęp backendowej części projektu e-commerce. Jest to plan kontrolny, a nie opis istniejących endpointów. Faktyczny kontrakt API jest w [api-contract.md](api-contract.md), obecna architektura w [architecture.md](architecture.md), a zasady bazy w [database.md](database.md).

Źródło wymagań: plan produktu przekazany przez właściciela projektu 20 października 2026 r. Podział na moduły i kolejność poniżej są propozycją organizacji prac dla dwuosobowego zespołu. Stan kodu sprawdzono 20 października 2026 r.

## Jak używać tego pliku

- `[x]` oznacza pracę potwierdzoną w repozytorium; `[ ]` oznacza pracę jeszcze niepotwierdzoną jako ukończona. Samo utworzenie zadania w Jirze nie zamyka punktu.
- Po ukończeniu punktu dopisz przy nim klucz zadania Jiry lub odnośnik do PR, jeśli istnieje. Status **etapu** w tabeli aktualizuj ręcznie: **nie rozpoczęto**, **w toku**, **gotowe** albo **zablokowane**. Etap jest „gotowy”, gdy działa opisany rezultat i sprawdzono dotyczące go zadania z listy pod tabelą.
- Decyzje zapisuj w sekcji „Decyzje otwarte”, a po uzgodnieniu przenieś wynik do odpowiedniego modułu. Nie zamieniaj propozycji w fakt bez ustalenia z zespołem.
- Ten plik śledzi backend i punkty styku z frontendem. Szczegółowe zadania, właściciele i bieżący sprint są w Jirze; nie trzeba kopiować tutaj całej tablicy.

## Stan na dziś

Backend ma już ASP.NET Core na .NET 10, lokalny Docker, EF Core z PostgreSQL, profil użytkownika oraz rejestrację, logowanie i reset hasła przez Supabase Auth. E-mail resetu jest wysyłany przez Mailtrap Transactional Email API. Istnieje projekt testów xUnit. Limity żądań obejmują obecnie dwa endpointy resetu hasła.

Nie ma jeszcze funkcji sklepu: produktów, wariantów, koszyka, zamówień, promocji, historii cen, magazynu, płatności, dostawy ani panelu administracyjnego. Nie ma też logowania Google, Redis, Supabase Storage, GlitchTip, zadania czyszczącego ani `/healthz`. Frontend jest wskazany w README jako osobne repozytorium; decyzja o docelowym układzie repozytoriów pozostaje otwarta.

## Mniejsze etapy i postęp

Numery przed kropką wskazują moduł opisany dalej. Etapy w ramach modułu są ustawione w sugerowanej kolejności. **Nie trzeba kończyć całego modułu, żeby zacząć niezależny etap z innego modułu.** Przy planowaniu sprintu wybierzcie najbliższy etap, a szczegółowe zadania zapiszcie w Jirze.

| Etap | Rezultat, który można sprawdzić | Status |
| --- | --- | --- |
| **0.1 Fundament techniczny** | API .NET 10, Docker i projekt testowy istnieją w repozytorium | Gotowe w kodzie |
| **0.2 Logowanie i profil** | E-mail/hasło, reset hasła i profil działają w kodzie | Gotowe w kodzie |
| **0.3 Próba na prawdziwym środowisku** | Dwa konta i frontend przechodzą uzgodnione próby sesji i dostępu | Nie rozpoczęto |
| **1.1 Ścieżki użytkownika** | Spisany przebieg od listy produktów do historii zamówienia | Nie rozpoczęto |
| **1.2 Kontrakt pierwszego widoku** | Frontend zna pola listy i szczegółów produktu oraz format błędów | Nie rozpoczęto |
| **1.3 Hostowanie i sesja** | Uzgodnione adresy, cookies, CORS, CSRF i zasady pracy za proxy | Nie rozpoczęto |
| **2.1 Model katalogu** | Uzgodnione produkty, warianty i drzewo kategorii; gotowa migracja | Nie rozpoczęto |
| **2.2 Wprowadzanie oferty** | Administrator może dodać i zmienić produkt oraz wariant | Nie rozpoczęto |
| **2.3 Odczyt katalogu** | Klient widzi listę i szczegóły produktu | Nie rozpoczęto |
| **2.4 Wyszukiwanie** | Klient wyszukuje i filtruje ofertę przez API | Nie rozpoczęto |
| **3.1 Cena i historia** | Każda zmiana ceny jest zapisywana do historii | Nie rozpoczęto |
| **3.2 Reguły promocji** | Działają ustalone zniżki kwotowe i procentowe w uzgodnionym zakresie | Nie rozpoczęto |
| **3.3 Dane wcześniejszej ceny** | API zwraca dane do pokazania najniższej ceny z 30 dni przed obniżką | Nie rozpoczęto |
| **4.1 Koszyk gościa** | Gość dodaje, zmienia i usuwa pozycje | Nie rozpoczęto |
| **4.2 Koszyk konta i przejęcie** | Koszyk gościa trafia na właściwe konto po uwierzytelnieniu | Nie rozpoczęto |
| **4.3 Ulubione** | Gość i klient mogą zapisać produkty zgodnie z ustaloną zasadą przejęcia | Nie rozpoczęto |
| **4.4 Logowanie Google** | Konto Google prowadzi do właściwego profilu i zasobów klienta | Nie rozpoczęto |
| **5.1 Zamówienie i checkout** | Zalogowany klient zapisuje zamówienie z ceną i wariantem z chwili zakupu | Nie rozpoczęto |
| **5.2 Kontrola magazynu** | Równoległe zakupy i nieudana płatność nie psują stanu | Nie rozpoczęto |
| **5.3 Historia zakupów** | Klient widzi tylko swoje zamówienia | Nie rozpoczęto |
| **6.1 Tpay sandbox** | Testowa płatność bezpiecznie aktualizuje zamówienie | Nie rozpoczęto |
| **6.2 Symulowana dostawa** | Zamówienie przechodzi przez ustalone statusy kurierskie | Nie rozpoczęto |
| **6.3 E-maile transakcyjne** | Uzgodnione zdarzenia wysyłają odpowiednie wiadomości | Nie rozpoczęto |
| **7.1 Uprawnienia administratora** | Zwykły klient nie ma dostępu do operacji zarządzania | Nie rozpoczęto |
| **7.2 Zarządzanie sklepem** | Panel obsługuje ofertę, ceny, stany i zamówienia | Nie rozpoczęto |
| **7.3 Pliki produktów** | Zdjęcia są obsługiwane przez Supabase Storage z właściwym dostępem | Nie rozpoczęto |
| **8.1 Zdrowie, logi i błędy** | Działa `/healthz`, logi JSON i raportowanie do GlitchTip | Nie rozpoczęto |
| **8.2 Limity żądań** | Uzgodnione operacje mają limity i czytelne odpowiedzi dla frontendu | W toku: reset hasła |
| **8.3 Cache Redis** | Wybrane odczyty korzystają z cache i są poprawnie unieważniane | Nie rozpoczęto |
| **8.4 Czyszczenie danych** | Zadanie cykliczne usuwa tylko dane z zatwierdzoną retencją | Nie rozpoczęto |
| **9.1 Testy pełnych ścieżek** | Przechodzą testy backendu i uzgodnione scenariusze E2E | Nie rozpoczęto |
| **9.2 Wydanie demonstracyjne** | Całą wybraną ścieżkę da się pokazać na wspólnym środowisku | Nie rozpoczęto |

Pierwszy praktyczny ciąg prac to **1.1 → 1.2 → 1.3 → 2.1 → 7.1 → 3.1 → 2.2 → 2.3 → 2.4**. Numery 7.1 i 3.1 pojawiają się wcześniej, bo uprawnienia administratora są potrzebne przed zapisem oferty, a historia cen przed jej zmianą. Próbę **0.3** wykonajcie po ustaleniu środowiska z **1.3**. Etapy **8.1** i potrzebną część **8.2** warto włączyć przed wspólnym środowiskiem testowym. Pozostałe zależności i szczegółowe prace opisują moduły niżej.

## 0. Istniejący fundament

- [x] ASP.NET Core na .NET 10, projekt testowy xUnit i lokalny Docker.
- [x] Rejestracja i logowanie e-mail/hasło przez Supabase Auth, sesja w chronionym ciasteczku, wylogowanie i reset hasła.
- [x] Profil użytkownika w PostgreSQL przez EF Core; migracje aplikacji nie zarządzają `auth.users`.
- [x] Wysyłka e-maila resetu hasła przez Mailtrap API.
- [ ] Sprawdzić przepływ dwóch prawdziwych kont i uprawnienia bazy według [user-profiles.md](user-profiles.md) oraz [testing.md](testing.md).
- [ ] Potwierdzić działanie sesji, ciasteczek i resetu hasła w docelowym układzie frontendu i API.

**Warunek zakończenia:** istniejące funkcje są sprawdzone na uzgodnionym środowisku, a znane ograniczenia są zapisane w dokumentacji. Status „w toku” nie oznacza, że działające już endpointy trzeba pisać ponownie.

## 1. Ustalenia i kontrakt frontend–API

- [ ] Opisać główne ścieżki użytkownika: oglądanie oferty, koszyk gościa, przejście na konto, zakup oraz obsługa zamówienia.
- [ ] Ustalić sposób hostowania frontendu względem API i przejrzeć wynikające z niego cookies, CORS, CSRF oraz przekazywanie adresu klienta za reverse proxy.
- [ ] Uzgodnić wspólne zasady kontraktu: format błędów, stronicowanie, filtrowanie, daty, kwoty i walutę.
- [ ] Dla pierwszego wycinka ustalić zakres danych listy produktów i strony produktu; po wdrożeniu dopisać rzeczywiste trasy do [api-contract.md](api-contract.md).
- [ ] Rozstrzygnąć, czy zachować dwa repozytoria, czy przejść na monorepo. Sama zmiana organizacji repozytoriów nie blokuje modelowania katalogu.

**Warunek zakończenia:** obie osoby umieją połączyć pierwszy widok sklepu z API bez zgadywania formatu danych i sposobu logowania.

## 2. Katalog, warianty i wyszukiwanie

- [ ] Ustalić model produktu, wariantu oraz zagnieżdżonych kategorii i podkategorii, w tym identyfikatory i zasady widoczności.
- [ ] Przygotować mapowania EF Core i migracje wyłącznie dla danych aplikacji; sprawdzić SQL migracji, uprawnienia i zasady dostępu.
- [ ] Dodać minimalne operacje administracyjne potrzebne do wprowadzenia i aktualizacji oferty.
- [ ] Udostępnić listę produktów z filtrowaniem/stronicowaniem i osobny odczyt szczegółów produktu.
- [ ] Dodać wyszukiwanie przy użyciu mechanizmów PostgreSQL; ustalić, jakie pola są wyszukiwane i jak ocenić trafność wyników.
- [ ] Związać stan magazynowy z właściwym poziomem oferty (produkt lub wariant) po podjęciu tej decyzji.
- [ ] Dodać testy reguł katalogu i sprawdzić rzeczywiste połączenie widoków frontendu z API.

**Warunek zakończenia:** można wprowadzić produkt z wariantem i kategorią, znaleźć go na liście oraz otworzyć jego szczegóły.

## 3. Ceny, historia i promocje

- [ ] Ustalić, czy cena i promocja dotyczą produktu czy wariantu, czym jest „kolekcja”, jak zaokrąglać kwoty oraz co robić przy nakładających się promocjach.
- [ ] Zaprojektować i wdrożyć zapis historii cen **przed** uruchomieniem administracyjnej zmiany cen lub pokazywaniem obniżek.
- [ ] Dodać promocje procentowe i kwotowe dla pojedynczych produktów, kolekcji albo całej oferty, zgodnie z uzgodnionymi zasadami pierwszeństwa.
- [ ] Udostępnić frontendowi cenę bieżącą oraz dane do pokazania najniższej ceny z 30 dni przed obniżką wszędzie tam, gdzie jest prezentowana promocja.
- [ ] Zweryfikować interpretację wymogu Omnibus dla wariantów i następujących po sobie zmian cen przed wdrożeniem reguł do kodu.
- [ ] Przetestować granice czasu, nakładające się promocje, zmianę ceny w trakcie koszyka i przypadki bez pełnej historii.

**Warunek zakończenia:** ten sam wariant ma spójnie obliczaną cenę w katalogu i przy zakupie; historia potrzebna do prezentacji obniżki nie znika po czyszczeniu danych.

## 4. Konto, koszyk i ulubione

- [ ] Wybrać sposób identyfikacji i przechowywania koszyka oraz ulubionych gościa, bez przypisywania ich do przypadkowego konta.
- [ ] Dodać operacje koszyka i ulubionych dla gościa oraz zalogowanego użytkownika.
- [ ] Ustalić moment przejęcia koszyka po rejestracji/logowaniu i reguły scalania duplikatów. Obecna rejestracja nie wystawia sesji, więc samo jej zakończenie nie potwierdza tożsamości dla API.
- [ ] Przy każdym odczycie i zapisie zasobów konta używać zweryfikowanego ID użytkownika; sprawdzić brak dostępu do danych innej osoby.
- [ ] Obsłużyć zmianę ceny, wycofany wariant, brak stanu, dwa urządzenia i ponowienie żądania.
- [ ] Dodać logowanie Google przez Supabase Auth po ustaleniu przekierowań i zasad łączenia tożsamości; sprawdzić, czy prowadzi do tego samego profilu i koszyka.

**Warunek zakończenia:** gość może zapamiętać wybór, a po uwierzytelnieniu widzi go na swoim koncie; cudze dane pozostają niedostępne.

## 5. Checkout, zamówienia i magazyn

- [ ] Ustalić kroki checkoutu, wymagane dane klienta i adresu oraz sposób potwierdzania finalnej kwoty.
- [ ] Dodać model zamówienia i pozycji, który zachowuje wybrany wariant, cenę i inne potrzebne dane z chwili zakupu.
- [ ] Utworzenie zamówienia ograniczyć do zalogowanego klienta i zweryfikować właściciela przy odczycie historii.
- [ ] Ustalić moment rezerwacji lub pomniejszenia stanu, czas rezerwacji i reakcję na porzucony koszyk, nieudaną płatność oraz równoległe zakupy ostatniej sztuki.
- [ ] Wdrożyć spójne zmiany zamówienia i stanu magazynowego oraz ochronę przed podwójnym złożeniem tego samego zamówienia.
- [ ] Udostępnić historię zamówień klienta i testy najważniejszych scenariuszy poprawnych oraz błędnych.

**Warunek zakończenia:** zalogowany użytkownik może złożyć zamówienie na dostępny produkt, a jego historia i stan magazynu pozostają spójne.

## 6. Płatność Tpay, dostawa i wiadomości

- [ ] Zintegrować wyłącznie sandbox Tpay; ustalić statusy transakcji i sposób weryfikacji wyniku po stronie backendu.
- [ ] Obsłużyć ponowione, opóźnione i niepoprawne powiadomienia o płatności bez podwójnej zmiany zamówienia.
- [ ] Dodać jedną metodę dostawy: kuriera. Status dostawy zmieniać w symulowanym procesie bez integracji z API przewoźnika; wybór firmy pozostaje otwarty.
- [ ] Określić, jakie wiadomości transakcyjne są potrzebne po zakupie i jakie zdarzenie uruchamia każdą z nich.
- [ ] Rozstrzygnąć, czy wymagany „serwer pocztowy” oznacza SMTP, czy obecne Mailtrap Transactional Email API może służyć również do nowych wiadomości.
- [ ] Sprawdzić pełny przebieg: zamówienie → płatność testowa → status zamówienia → symulowana dostawa → wiadomość.

**Warunek zakończenia:** cały zakup można przejść na środowisku demonstracyjnym bez pobrania prawdziwej płatności.

## 7. Administracja i pliki

- [ ] Ustalić, kto jest administratorem i na jakiej podstawie API nadaje dostęp; nie opierać uprawnień na danych, które użytkownik może sam edytować.
- [ ] Rozbudować endpointy do zarządzania katalogiem, wariantami, kategoriami, cenami, promocjami, stanami i zamówieniami.
- [ ] Ustalić zasady przesyłania, odczytu i usuwania plików w Supabase Storage, zwłaszcza zdjęć produktów.
- [ ] Sprawdzić dostęp zwykłego klienta do operacji administracyjnych i plików; przetestować najważniejsze zmiany stanu.
- [ ] Uzgodnić kontrakt z frontendowym panelem administracyjnym w stylu CRM.

**Warunek zakończenia:** administrator może obsłużyć sklep przez panel, a zwykły klient nie może wykonać operacji administracyjnych.

## 8. Utrzymanie, bezpieczeństwo i obserwowalność

- [x] Limity żądań dla prośby o reset hasła i potwierdzenia resetu.
- [ ] Rozszerzyć rate limiting na uzgodnione operacje; ustalić progi, odpowiedź API i sposób komunikatu we frontendzie.
- [ ] Dodać `/healthz` oraz sprawdzić, co endpoint ma potwierdzać na danym środowisku.
- [ ] Wprowadzić logi JSON w .NET/Dockerze i monitorowanie błędów backendu w GlitchTip; frontend ma własną integrację.
- [ ] Dodać Redis tylko dla wybranych odczytów po ustaleniu kluczy, czasu życia i unieważniania po zmianie danych.
- [ ] Ustalić dokładnie, które dane wolno czyścić po 30 dniach, a następnie wdrożyć zadanie cykliczne. Nie usuwać historii cen potrzebnej do wymogu Omnibus ani danych, których retencja pozostaje nierozstrzygnięta.
- [ ] Przed środowiskiem publicznym sprawdzić konfigurację sekretów, kluczy Data Protection, połączenia z bazą i zachowanie za reverse proxy.

**Warunek zakończenia:** działanie API można monitorować, ograniczać nadużycia i diagnozować błędy, a czyszczenie nie niszczy danych potrzebnych sklepowi.

## 9. Testy przekrojowe i wydanie demonstracyjne

- [ ] Dla każdego ukończonego modułu mieć odpowiednie testy jednostkowe reguł i testy integracyjne granic API/bazy.
- [ ] Z frontendem przejść scenariusze E2E: wyszukanie produktu, koszyk gościa, zalogowanie, checkout, płatność testowa, historia zamówienia i działania administratora.
- [ ] Sprawdzić scenariusze błędne: brak stanu, zmiana ceny, nieudana płatność, ponowione powiadomienie, brak uprawnień i niedostępna usługa.
- [ ] Uruchomić API w Dockerze ze środowiskową konfiguracją oraz sprawdzić `/healthz`, logi i raportowanie błędów.
- [ ] Uzgodnić zakres wydania demonstracyjnego; świadomie wyłączyć lub oznaczyć funkcje, które nie są jeszcze gotowe.

**Warunek zakończenia:** obie osoby potrafią przejść uzgodnioną ścieżkę sklepu na wspólnym środowisku i wskazać znane ograniczenia.

## Decyzje otwarte

| Decyzja | Potrzebna przed | Stan |
| --- | --- | --- |
| Docelowy układ repozytoriów, framework frontendu i adresy środowisk | 1; zasady cookies przed 4 | Otwarta |
| Format kwot/waluta oraz zasady wariantów i kategorii | 2–3 | Otwarta |
| Kolekcje, nakładanie promocji, zaokrąglanie i interpretacja wymogu Omnibus | 3 | Otwarta |
| Przechowywanie i scalanie koszyka oraz ulubionych gościa | 4 | Otwarta |
| Rezerwacja stanu i reakcja na nieudaną płatność | 5–6 | Otwarta |
| Statusy dostawy, przewoźnik i zakres danych adresowych | 5–6 | Otwarta |
| SMTP czy Mailtrap API oraz lista wiadomości | 6 | Otwarta |
| Zakres danych usuwanych po 30 dniach | 8: zadanie cykliczne | Otwarta |
| Zakres cache Redis, progów limitów i metryk | 8 | Otwarta |

Po decyzji wpisz wynik i datę w odpowiednim module, a szczegóły techniczne w dokumencie właściwego obszaru. Nie traktuj tej tabeli jako zgody na implementację konkretnego wariantu.
