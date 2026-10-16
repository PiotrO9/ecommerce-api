# Instrukcje dla Codex

To repozytorium zawiera backend aplikacji e-commerce napisany w ASP.NET Core na .NET 10.

## Kontekst projektu

- `README.md` wskazuje lokalizację dokumentacji projektu.
- `docs/protected-areas.md` wskazuje obszary wymagające osobnego potwierdzenia przed zmianą.
- `docs/testing.md` opisuje układ projektu testowego, uruchamianie testów oraz zasady doboru scenariuszy.

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

## Obszary chronione

Podczas planowania zmian technicznych oraz przed ich wdrożeniem użyj skilla `.agents/skills/protected-area-check/SKILL.md`. Jeśli plan lub rzeczywisty zakres prac dotyka obszaru z `docs/protected-areas.md`, przedstaw użytkownikowi konkretny wpływ i uzyskaj potwierdzenie zgodnie ze skillem. Potwierdzenie planu nie zastępuje ponownego potwierdzenia przed zmianą chronionego obszaru.

## Propozycja nazwy commita

Gdy użytkownik poprosi o nazwę lub wiadomość commita, sprawdź zmiany, które mają być nim objęte (w tym pliki nieśledzone, jeśli są istotne), i zaproponuj jedną zwięzłą wiadomość w formacie Conventional Commits: `<type>[(scope)]: <description>`.

- Dobierz typ do faktycznej zmiany, np. `feat`, `fix`, `docs`, `refactor`, `test` lub `chore`. Scope dodaj tylko wtedy, gdy pomaga wskazać obszar zmiany.
- Opis powinien mówić, co zmienia commit. Nie sugeruj funkcji ani poprawek, których nie ma w zmianach.
- Oznaczenie `!` stosuj tylko dla potwierdzonej zmiany niekompatybilnej.
- Jeśli zakres commita jest niejasny, poproś o doprecyzowanie zamiast zgadywać.
- Sama prośba o nazwę commita nie oznacza zgody na wykonanie `git commit`.
