# Instrukcje dla Codex

To repozytorium zawiera backend aplikacji e-commerce napisany w ASP.NET Core na .NET 10.

## Kontekst projektu

- `README.md` wskazuje lokalizację dokumentacji projektu.
- `docs/protected-areas.md` wskazuje obszary wymagające osobnego potwierdzenia przed zmianą.
- `docs/testing.md` opisuje układ projektu testowego, uruchamianie testów oraz zasady doboru scenariuszy.
- `docs/docker.md` opisuje budowanie obrazu i uruchamianie API w kontenerze.
- `docs/auth-implementation-plan.md` opisuje plan nauki i wdrożenia uwierzytelniania z Supabase Auth oraz EF Core.
- `docs/user-profiles.md` opisuje powiązanie profilu aplikacji z kontem Supabase Auth przez EF Core.
- `docs/architecture.md` daje mapę obecnego backendu i granic między API, Supabase Auth i EF Core.
- `docs/api-contract.md` opisuje obecne trasy, żądania i odpowiedzi API.
- `docs/database.md` opisuje schematy PostgreSQL, mapowanie EF Core i zasady migracji.
- `docs/project-direction.md` jest plikiem kontrolnym planu backendu: moduły, lista prac, statusy i decyzje otwarte.
- `docs/catalog-model.md` zawiera wstępny model produktu, wariantu, cech i kategorii oraz relacje i pytania otwarte przed migracją.
- `docs/catalog-management-implementation-plan.md` opisuje administrację ofertą, reguły, endpointy i instrukcję Postmana: O0–O9 są w kodzie, O10 wymaga odbioru środowiskowego; migracje oferty sprawdzono lokalnie, bez zastosowania w Supabase.
- `docs/inventory-model.md` opisuje stan wariantu, rezerwacje, minimalne zamówienia, wygaszanie i serwerowe rozliczenie; migracja zdalnej bazy i integracja płatności pozostają osobnymi krokami.
- `docs/pricing-model.md` zawiera decyzje o cenie wariantu i koncepcję historii kolejnych zmian cen.
- `docs/promotion-implementation-plan.md` opisuje wdrożone promocje produktu, okresy oferowania, wcześniejszą cenę, wynik odbioru i granice pierwszej wersji.
- `docs/promotion-extension-plan.md` opisuje dynamiczne promocje katalogu i kategorii, wykluczenia, konflikty, automatyczną ponowną ocenę i etapy DP1–DP6; harmonogramy startu/końca oraz łączenie rabatów pozostają osobnym zakresem.
- `docs/collection-implementation-plan.md` opisuje wdrożone kolekcje i promocje kolekcji, model, endpointy, zgodność dawnych ponowień, etapy K1–K6 i wyniki odbioru.
- `docs/cart-favorites-implementation-plan.md` opisuje wdrożony backend oddzielnych koszyków gościa i konta bez scalania oraz ulubionych wariantu wyłącznie na koncie, etapy KU0–KU5, testy lokalne i odbiór 54/54 HTTPS z rzeczywistym Auth oraz pozostały odbiór frontendu i migrację Supabase.
- `docs/administrator-access.md` opisuje uprawnienia administratora, nadanie pierwszego dostępu i administracyjne endpointy cen.
- `docs/demo-seed.md` opisuje ręcznie uruchamiany, developerski seeder katalogu i jego zabezpieczenia.
- `docs/seeder-maintenance.md` określa, jak rozbudowywać seeder razem ze zmianami modelu bazy.
- `docs/catalog-performance-audit.md` zawiera pomiar odczytu katalogu na 5000 syntetycznych produktów przed optymalizacją i po niej.

Przed rozpoczęciem zadania przeczytaj odpowiednie pliki. Kod i konfiguracja są źródłem prawdy o obecnym działaniu aplikacji. Jeśli dokumentacja i kod się różnią, wskaż rozbieżność i zaktualizuj dokumentację po ustaleniu stanu faktycznego.

## Dodawanie dokumentacji

- Dodaj nowy plik w `docs/`, gdy pojawią się trwałe ustalenia o aplikacji lub sposobie jej rozwoju, do których warto wracać w kolejnych zadaniach. Nie twórz pustych plików ani dokumentów na zapas.
- Przed utworzeniem pliku sprawdź, czy informacja nie pasuje do istniejącego dokumentu. Jeden plik powinien mieć jeden jasny temat; użyj krótkiej nazwy w `kebab-case`, np. `api-contract.md`.
- Na początku pliku podaj tytuł i cel. Oddziel stan potwierdzony w kodzie od planów, propozycji i pytań otwartych. Przy ważnych ustaleniach zapisz powód decyzji oraz źródło, jeśli jest dostępne.
- Po zmianie kodu zaktualizuj dokumenty, których treść przestała być aktualna. Gdy nowy plik staje się stałym źródłem kontekstu dla Codex, dodaj odnośnik do niego w sekcji „Kontekst projektu” w `AGENTS.md`.

## Praca w repozytorium

- Nie zakładaj, że planowana funkcja już istnieje.
- Nie dopisuj niepotwierdzonych wymagań, integracji ani decyzji jako faktów. Oznacz je jako propozycje lub pytania otwarte.
- Po zmianach w kodzie uruchom `dotnet build` i podaj wynik. Uruchom odpowiednie testy, jeśli zostaną dodane.
- Nie zapisuj w dokumentacji sekretów ani danych klientów.
- Przy zmianie encji, migracji, wymaganych pól lub relacji sprawdź `docs/seeder-maintenance.md`. Jeśli nowe dane należą do scenariuszy demonstracyjnych aplikacji, zaktualizuj seeder, jego liczniki i dokumentację w tym samym zadaniu; wyjątek uzasadnij w dokumentacji.

## Obszary chronione

Podczas planowania zmian technicznych oraz przed ich wdrożeniem użyj skilla `.agents/skills/protected-area-check/SKILL.md`. Jeśli plan lub rzeczywisty zakres prac dotyka obszaru z `docs/protected-areas.md`, przedstaw użytkownikowi konkretny wpływ i uzyskaj potwierdzenie zgodnie ze skillem. Potwierdzenie planu nie zastępuje ponownego potwierdzenia przed zmianą chronionego obszaru.

## Propozycja nazwy commita

Gdy użytkownik poprosi o nazwę lub wiadomość commita, sprawdź zmiany, które mają być nim objęte (w tym pliki nieśledzone, jeśli są istotne), i zaproponuj jedną zwięzłą wiadomość w formacie Conventional Commits: `<type>[(scope)]: <description>`.

- Dobierz typ do faktycznej zmiany, np. `feat`, `fix`, `docs`, `refactor`, `test` lub `chore`. Scope dodaj tylko wtedy, gdy pomaga wskazać obszar zmiany.
- Opis powinien mówić, co zmienia commit. Nie sugeruj funkcji ani poprawek, których nie ma w zmianach.
- Oznaczenie `!` stosuj tylko dla potwierdzonej zmiany niekompatybilnej.
- Jeśli zakres commita jest niejasny, poproś o doprecyzowanie zamiast zgadywać.
- Sama prośba o nazwę commita nie oznacza zgody na wykonanie `git commit`.
