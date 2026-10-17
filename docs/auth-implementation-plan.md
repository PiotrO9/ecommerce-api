# Plan wdrożenia uwierzytelniania

Cel: przeprowadzić krok po kroku wdrożenie logowania w API oraz nauczyć się, co dzieje się przy rejestracji, logowaniu, weryfikacji tokenu i dostępie do danych użytkownika.

## Stan potwierdzony w repozytorium

- API działa na ASP.NET Core i .NET 10. `Program.cs` rejestruje kontrolery, mapuje ich trasy i udostępnia `GET /`.
- `AuthController` obsługuje `POST /api/v1/web/auth/register` przez `SupabaseAuthClient`: po sukcesie zwraca `201` bez tokenów i ciasteczka, a znane błędy Supabase mapuje na odpowiedzi API. `POST /api/v1/web/auth/login` rozpoznaje `invalid_credentials` (`401`), limit żądań (`429`) i niedostępność usługi (`503`). Po poprawnym logowaniu zwraca `200` bez tokenów w treści i wystawia chronione ciasteczko. `GET /api/v1/web/auth/me` odczytuje je, sprawdza stały termin i weryfikuje access token przez `GET /auth/v1/user` w Supabase; następnie pobiera imię i nazwisko z profilu aplikacji. Zwraca ID, e-mail, `firstName` i `lastName`; dwa ostatnie pola są `null`, gdy profilu jeszcze nie ma. Gdy Supabase odrzuci access token (`401`/`403`), API wymienia refresh token na nową parę przez `POST /auth/v1/token?grant_type=refresh_token`, wystawia nowe ciasteczko ze starym terminem sesji i ponawia odczyt użytkownika. `POST /api/v1/web/auth/logout` wywołuje Supabase z `scope=local`; po odrzuceniu starego access tokenu też próbuje odświeżenia, aby zakończyć aktualną sesję. Wymaga nagłówka `X-Requested-With: XMLHttpRequest` jako ochrony przed prostym żądaniem CSRF.
- Kod błędu Supabase Auth odczytujemy z tekstowego pola `error_code`. W odpowiedzi zaobserwowanej przy błędnym logowaniu pole `code` było liczbą `400`; wcześniejsza próba deserializacji go jako tekstu kończyła się ogólnym `503` zamiast `401`. Tę samą strukturę błędu wykorzystują logowanie i rejestracja.
- Istnieją modele `LoginRequest` (Email, Password, RememberMe) i `RegisterRequest` (Email, Password, ConfirmPassword). Oba sprawdzają obecność i format e-maila oraz obecność hasła. `RegisterRequest` sprawdza ponadto długość hasła (6–50 znaków) i zgodność potwierdzenia.
- `Program.cs` odczytuje `Supabase:Url`, `Supabase:PublishableKey` i `ConnectionStrings:SupabaseDatabase`, wymaga ich obecności i rejestruje klienta HTTP Supabase Auth, ASP.NET Core Data Protection oraz `AppDbContext` z Npgsql. Opcjonalne `DataProtection:KeysPath` wskazuje katalog trwałych kluczy. Pierwsza migracja EF Core definiuje profil aplikacji i została zastosowana w bazie; pozostałe kontrole opisuje [user-profiles.md](user-profiles.md).
- `AuthCookieService` zapisuje parę tokenów Supabase i `AbsoluteExpiresAt` jako jedną wartość chronioną przez Data Protection w ciasteczku `__Host-ecomm-auth` (`HttpOnly`, `Secure`, `SameSite=Lax`, `Path=/`). Termin to 1 dzień lub 30 dni od logowania; ciasteczko ma ten sam termin. Przed zapisem sprawdzany jest limit 3800 bajtów dla nazwy i wartości; przy jego przekroczeniu logowanie zwraca `503`. Faktycznego rozmiaru z prawdziwą odpowiedzią Supabase jeszcze nie zmierzono.
- Wybór `SameSite=Lax` zakłada frontend i API działające w obrębie tej samej witryny. Jeśli będą działały na różnych witrynach, przed wdrożeniem trzeba ustalić politykę cookies, CORS i CSRF. Odświeżanie następuje po odpowiedzi `401`/`403` z Supabase, a nie według zegara. Definitywnie nieważny refresh token usuwa ciasteczko; przejściowa niedostępność Supabase pozostawia je do ponownej próby. Odświeżenie nie przedłuża `AbsoluteExpiresAt`. Ochronę CSRF i CORS trzeba ponownie ocenić przy dołączeniu frontendu i kolejnych endpointów zmieniających dane.
- Istnieje projekt testowy xUnit opisany w [testing.md](testing.md).
- Lista w [protected-areas.md](protected-areas.md) jest obecnie pusta. Przed każdą późniejszą edycją trzeba sprawdzić ją ponownie.

## Ustalenia i proponowany podział odpowiedzialności

Użytkownik chce najpierw rejestracji i logowania przez e-mail i hasło, a w przyszłości także przez Google i być może Apple. Do pracy z bazą danych aplikacji wybrał Entity Framework Core. Część etapów jest już wdrożona; dalsze zadania i pytania otwarte są opisane niżej.

Użytkownik wybrał w ustawieniach Supabase wariant wymagający małej litery, wielkiej litery, cyfry i symbolu oraz minimalną długość 6 znaków. Wyłączył wymaganie potwierdzenia e-maila po rejestracji. Kategorie znaków są egzekwowane przez Supabase Auth przy rejestracji i zmianie hasła. `RegisterRequest` nie powiela ich wyrażeniem regularnym, aby nie odrzucać symboli akceptowanych przez Supabase. Limit 50 znaków pozostaje dodatkową regułą API; nie wynika z pokazanego ustawienia Supabase. Endpoint rejestracji mapuje `weak_password` na ogólny komunikat o niespełnieniu wymagań.

Proponowany podział:

- **Supabase Auth** tworzy konto, sprawdza hasło i wydaje sesję. Nie tworzymy własnej tabeli haseł ani własnych tokenów dostępowych.
- **ASP.NET Core API** wystawia uzgodnione endpointy, weryfikuje token użytkownika i podejmuje decyzje o dostępie do zasobów.
- **EF Core + Npgsql** obsługuje dane sklepu w PostgreSQL. Nie zarządza tabelami należącymi do Supabase Auth, zwłaszcza `auth.users`.
- Pierwszy profil aplikacyjny zawiera imię i nazwisko. Jego kluczem jest identyfikator użytkownika Supabase (`sub` w tokenie), a nie e-mail ani nazwa dostawcy logowania. Dzięki temu różne metody logowania mogą prowadzić do tego samego konta. Mapowanie, zastosowaną migrację i endpointy opisuje [user-profiles.md](user-profiles.md).

Uzgodniony wariant dla przeglądarki: klient → API → Supabase Auth przy rejestracji i logowaniu. API otrzymuje access token i refresh token Supabase, a następnie umieszcza je w jednym zaszyfrowanym i zabezpieczonym przed zmianą ciasteczku `HttpOnly; Secure`. Nie utrzymuje osobnej tabeli sesji aplikacji ani dodatkowego identyfikatora sesji. Przy zwykłych żądaniach przeglądarka wysyła ciasteczko do API; API odzyskuje tokeny i sprawdza wymagane warunki dostępu. JavaScript frontendu nie odczytuje tokenów i nie wywołuje Supabase bezpośrednio. Dokładny przepływ dla ewentualnego klienta mobilnego pozostaje otwarty.

Uzgodniona polityka czasu: bez zaznaczonego `rememberMe` maksymalny czas od logowania wynosi 1 dzień; z zaznaczonym `rememberMe` — 30 dni. Obie opcje wystawiają trwałe ciasteczko, które może przetrwać zamknięcie przeglądarki. Użytkownik wybrał stały termin: aktywność i odświeżenie tokenów nie przedłużają go. Termin zapisujemy w chronionej zawartości ciasteczka i sprawdzamy na backendzie. Nie dodajemy przesuwanego limitu bezczynności. Propozycja dla access tokenu: początkowo pozostawić domyślny czas Supabase (zwykle 1 godzina); faktyczne ustawienie projektu wymaga sprawdzenia.

Ten termin ogranicza dostęp **przez nasze API**. Samo wygaśnięcie ciasteczka w przeglądarce nie informuje Supabase o zakończeniu jego sesji. Jeśli potrzebne będzie wymuszone zakończenie sesji u dostawcy dokładnie po tym czasie, należy osobno ocenić dostępne ustawienia czasu sesji Supabase albo mechanizm po stronie serwera.

Równoległe żądania z tym samym starym refresh tokenem pozostają zależne od zasad ponownego użycia tokenu w Supabase i kolejności odpowiedzi HTTP w przeglądarce. Test lokalny sprawdza przypadek, w którym oba odświeżenia zwracają tę samą aktywną parę. Przed użyciem wielu instancji API trzeba sprawdzić ten scenariusz z prawdziwym Supabase; aplikacja nie ma własnej koordynacji odświeżeń ani magazynu sesji. Źródło: [Supabase: sesje i ponowne użycie refresh tokenu](https://supabase.com/docs/guides/auth/sessions).

Uzasadnienie: ten wariant pozwala zacząć bez własnego magazynu sesji, a tokeny pozostają niedostępne dla JavaScriptu. Ciasteczko nadal jest poufnym poświadczeniem. Wymaga ochrony przed CSRF, trwałych kluczy szyfrujących ASP.NET Core, poprawnego odświeżania i wymiany ciasteczka oraz oceny limitu wielkości ciasteczek. Dla operacji wymagających natychmiastowego odebrania dostępu trzeba zaplanować dodatkową kontrolę sesji. Źródło: [RFC 10017, sekcja 6.1](https://datatracker.ietf.org/doc/html/rfc10017#section-6.1).

## Sposób pracy

Każdy etap jest osobnym, małym zadaniem. Codex najpierw podaje cel, scenariusz i pytanie sprawdzające zrozumienie. Użytkownik proponuje rozwiązanie i pisze kod. Codex sprawdza kod, uruchomione weryfikacje oraz scenariusze błędne. W razie trudności najpierw daje wskazówkę, później bardziej konkretny trop; pełne rozwiązanie dopiero na wyraźną prośbę. Do następnego etapu przechodzimy po omówieniu poprzedniego. Nie implementujemy kolejnych etapów za użytkownika z wyprzedzeniem.

Po każdej zmianie kodu uruchamiamy `dotnet build`; po dodaniu testów również odpowiednie `dotnet test`. Przed edycją ponownie sprawdzamy obszary chronione.

## Etapy i kryteria ukończenia

Nazwy endpointów `/auth/...` poniżej są skrótami; w obecnym kodzie pełny prefiks to `/api/v1/web/auth/`.

Najbliższe zadanie użytkownika: zweryfikować metadane migracji profilu i przetestować odczyt oraz zapis profilu na dwóch kontach. Ponadto przetestować prawdziwe logowanie, `/me` po wygaśnięciu access tokenu i `/logout` z Supabase oraz zmierzyć rozmiar otrzymanego ciasteczka. Testy z atrapą Supabase obejmują odświeżenie, utrzymanie stałego terminu, nieważny refresh token, niedostępność usługi i równoległe żądania, ale nie potwierdzają zachowania projektu Supabase ani kilku instancji API. Osobno sprawdzić działanie rejestracji dla poprawnych danych, słabego hasła, istniejącego adresu i niedostępności Supabase. Politykę siły hasła stosować przy rejestracji zgodnie z ustawieniami Supabase, nie odrzucać poprawnych danych logowania przez nową politykę rejestracji.

### 0. Model działania, bez kodu — rozpoczęty

**Zadanie użytkownika:** narysować przepływ rejestracji, logowania i wywołania chronionego endpointu. Wyjaśnić różnicę między kontem, sesją, access tokenem, refresh tokenem, uwierzytelnieniem i autoryzacją. Wyjaśnić, dlaczego zamówienie powinno wskazywać identyfikator użytkownika, a nie e-mail.

**Gotowe, gdy:** potrafi wskazać, kto sprawdza hasło, kto wystawia token i co API musi sprawdzić przed zwróceniem prywatnych danych.

### 1. Konfiguracja i kontrakt

**Zadanie użytkownika:** sprawdzić w swoim projekcie Supabase ustawienia rejestracji, potwierdzania e-maila, czas życia JWT i typ klucza podpisującego; zaproponować żądania i odpowiedzi dla rejestracji, logowania, `GET /auth/me` i wylogowania. Dodać konfigurację adresu projektu i klucza publikowalnego bez umieszczania sekretów w repozytorium. Dla przyszłego EF Core przygotować osobne miejsce na connection string, ale połączenie z bazą dodać później.

**Gotowe, gdy:** znany jest kontrakt API i ustawienia projektu; API uruchamia się przy poprawnej konfiguracji i zgłasza czytelny błąd przy brakującej konfiguracji. Żadne hasło, connection string, klucz tajny ani token nie trafia do commita ani logów.

### 2. Rejestracja hasłem

**Zadanie użytkownika:** zaproponować kontrakt i napisać `POST /auth/register` wywołujące Supabase Auth. Ustalić odpowiedź dla scenariusza z włączonym potwierdzaniem e-maila. Nie zakładać, że rejestracja zawsze zwraca aktywną sesję ani że odpowiedź zawsze ujawnia istnienie adresu.

**Gotowe, gdy:** działają scenariusze: poprawne dane, niepoprawne dane, istniejący adres, niedostępność Supabase. Odpowiedź nie zwraca hasła ani nie ujawnia niepotrzebnych danych.

### 3. Logowanie hasłem i ciasteczko

**Zadanie użytkownika:** napisać `POST /auth/login` z wejściem `email`, `password`, `rememberMe`; rozdzielić błędne dane logowania od błędu usługi. Po otrzymaniu tokenów Supabase wystawić jedno ciasteczko z zawartością chronioną przez ASP.NET Core Data Protection, bez zwracania tokenów w JSON. Zapisać w chronionej zawartości nieprzesuwalny `absoluteExpiresAt`: roboczo 1 dzień albo 30 dni od logowania. Ustawić czas życia ciasteczka zgodnie z tym terminem, `HttpOnly`, `Secure` oraz odpowiedni `SameSite`; dodać ochronę przed CSRF dla żądań zmieniających dane. Zaplanować trwałe przechowywanie kluczy Data Protection przy ponownym uruchomieniu i wdrożeniu.

**Gotowe, gdy:** poprawne logowanie dla obu wartości `rememberMe` ustawia ciasteczko z właściwym terminem, przeglądarka wysyła je przy kolejnych żądaniach, a błędne hasło i konto niepotwierdzone dają przewidywalne odpowiedzi. Test obejmuje odczyt ciasteczka po restarcie aplikacji oraz jego rzeczywisty rozmiar. Hasło i tokeny nie są logowane.

### 4. Chroniony endpoint i weryfikacja JWT

**Zadanie użytkownika:** dodać `GET /auth/me` uwierzytelniane ciasteczkiem. Ochrona ciasteczka i wynik logowania nie zastępują sprawdzenia ważności sesji Supabase przy dostępie do API. Weryfikacja access tokenu powinna obejmować podpis, oczekiwanego wystawcę, odbiorcę i czas ważności; przy wygaśnięciu obsługa musi przejść przez odświeżenie. Sposób pozyskania kluczy zależy od faktycznego typu klucza podpisującego w projekcie Supabase; nie implementować kryptografii samodzielnie.

**Gotowe, gdy:** brak ciasteczka, uszkodzone ciasteczko, błędny token lub przekroczony `absoluteExpiresAt` nie dają dostępu; poprawna sesja pozwala odczytać identyfikator konta. Użytkownik rozumie różnicę między `401` i `403`. Samo odnowienie ciasteczka przez middleware nie może ominąć terminu Supabase ani `absoluteExpiresAt`.

### 5. Odświeżanie tokenów

**Zadanie użytkownika:** przy chronionym żądaniu użyć ważnego access tokenu, a blisko jego wygaśnięcia lub po wygaśnięciu wymienić refresh token w Supabase. Po udanej wymianie zapisać **nową parę** w nowym ciasteczku. Zachować pierwotny `absoluteExpiresAt`, a pozostały czas ciasteczka obliczać od niego. Przy definitywnie nieważnej sesji lub refresh tokenie usunąć ciasteczko i zwrócić `401`. Chwilową niedostępność Supabase lub timeout obsłużyć jako błąd usługi, bez automatycznego kasowania ciasteczka. Sprawdzić równoczesne żądania z kilku kart, aby nie zgubić najnowszego refresh tokenu.

**Gotowe, gdy:** użytkownik nie traci sesji tylko dlatego, że access token wygasł, ale sesja kończy się po 1 lub 30 dniach od logowania niezależnie od liczby odświeżeń. Scenariusze obejmują udaną wymianę, nieważny refresh token, równoległe żądania oraz chwilową niedostępność Supabase.

### 6. Wylogowanie i zabezpieczenia

**Zadanie użytkownika:** dodać wylogowanie, zakończyć właściwą sesję w Supabase i usunąć ciasteczko. Sprawdzić zakres wylogowania, CSRF, dopuszczone originy i brak tokenów w logach. Omówić, dlaczego już wydany access token może pozostać ważny do `exp`.

**Gotowe, gdy:** po wylogowaniu ciasteczko nie daje dostępu; żądanie z obcej strony nie wykonuje operacji zmieniającej dane; użytkownik rozumie ograniczenie natychmiastowego unieważnienia JWT.

### 7. EF Core i dane aplikacji

**Zadanie użytkownika:** dodać EF Core z dostawcą Npgsql i połączenie z PostgreSQL. Pierwsza tabela aplikacyjna to profil z imieniem i nazwiskiem, powiązany przez UUID z `auth.users.id`. Migracje EF mogą tworzyć i zmieniać wyłącznie obiekty aplikacji; nie mogą modyfikować tabeli `auth.users`. Profil powinien być dostępny przez własne uwierzytelnione endpointy API. Zakres innych danych profilu pozostaje otwarty.

**Gotowe, gdy:** migracja dotyczy tylko tabel aplikacji; dwa konta nie mogą odczytać ani zmienić cudzych danych. Przed wdrożeniem dostępu do danych trzeba określić rolę używaną przez połączenie EF i sprawdzić jej uprawnienia. Połączenie EF przez PostgreSQL nie przenosi automatycznie tożsamości użytkownika HTTP do polityk Supabase RLS. Jeśli tabela jest dostępna także przez Supabase Data API, trzeba osobno skonfigurować uprawnienia i RLS.

### 8. Kolejne metody logowania

**Zadanie użytkownika:** po działającym logowaniu hasłem dodać Google, a potem ewentualnie Apple. Dla każdej metody zaplanować konfigurację dostawcy, adresy przekierowań, callback oraz powiązanie tożsamości. Wybór przepływu zależy od tego, czy klient jest webowy, mobilny czy oba.

**Gotowe, gdy:** użytkownik zalogowany dowolną obsługiwaną metodą wywołuje te same chronione endpointy jako to samo konto, zgodnie z ustaloną polityką łączenia tożsamości. Nie tworzymy z góry pustych klas dla przyszłych dostawców.

## Otwarte decyzje przed implementacją

1. Jaki klient korzysta z API: aplikacja webowa, mobilna czy obie? W jakiej technologii?
2. Potwierdzanie e-maila jest obecnie wyłączone w projekcie Supabase według informacji użytkownika. Przed publicznym wdrożeniem trzeba zdecydować, czy je włączyć i obsłużyć stan „sprawdź pocztę”.
3. Czy oprócz przeglądarki powstanie klient mobilny lub inny klient wywołujący API bezpośrednio? Uzgodnione ciasteczko dotyczy przepływu przeglądarkowego przez API.
4. Profil aplikacyjny jest potrzebny. Na początek zawiera imię i nazwisko; dodatkowe pola można dodać późniejszą migracją.
5. Gdzie będzie uruchomione API? Od tego zależy dobór połączenia bezpośredniego lub poolera do PostgreSQL.
6. Zmierzyć wielkość chronionego ciasteczka z prawdziwymi tokenami przed utrwaleniem wariantu bez magazynu sesji aplikacji. Stałe limity 1 dzień / 30 dni zostały uzgodnione.

## Źródła dla decyzji technicznych

- [Supabase: zarządzanie danymi użytkowników](https://supabase.com/docs/guides/auth/managing-user-data) — granica `auth.users` i tabel aplikacji.
- [Supabase: JWT](https://supabase.com/docs/guides/auth/jwts) oraz [Microsoft: JWT Bearer w ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/configure-jwt-bearer-authentication?view=aspnetcore-10.0) — weryfikacja tokenu.
- [Supabase: konfiguracja Auth](https://supabase.com/docs/guides/auth/general-configuration) i [łączenie tożsamości](https://supabase.com/docs/guides/auth/auth-identity-linking) — potwierdzenie e-maila i wiele metod logowania.
- [Supabase: połączenie z PostgreSQL](https://supabase.com/docs/guides/database/connecting-to-postgres), [RLS](https://supabase.com/docs/guides/database/postgres/row-level-security) i [Npgsql: EF Core](https://www.npgsql.org/efcore/) — baza aplikacji.
- [Supabase: sesje](https://supabase.com/docs/guides/auth/sessions), [Google](https://supabase.com/docs/guides/auth/social-login/auth-google) i [Apple](https://supabase.com/docs/guides/auth/social-login/auth-apple) — późniejsze etapy.
- [RFC 10017: aplikacje przeglądarkowe](https://datatracker.ietf.org/doc/html/rfc10017#section-6.1) i [Microsoft: uwierzytelnianie ciasteczkiem](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/cookie?view=aspnetcore-10.0) — sesja w zaszyfrowanym ciasteczku.
- [Microsoft: ochrona przed CSRF](https://learn.microsoft.com/aspnet/core/security/anti-request-forgery?view=aspnetcore-10.0) — żądania uwierzytelniane ciasteczkiem.
