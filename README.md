# 🛒 E-commerce API

![.NET 10](https://img.shields.io/badge/.NET-10-007ACC?style=flat&labelColor=000000&logo=dotnet&logoColor=white)

Projekt platformy e-commerce łączącej panel klienta z narzędziami do zarządzania sklepem. Docelowo ma obsługiwać ofertę, zamówienia oraz integracje z płatnościami i dostawami. To repozytorium zawiera backend w ASP.NET Core na .NET 10. Obecnie API obsługuje rejestrację i logowanie e-mail/hasło przez Supabase Auth oraz profil użytkownika zapisany w PostgreSQL przez EF Core. Funkcje sklepu są planowane.

Frontend: [E-commerce Web](https://github.com/piotrowski-patryk/ecommerce-web).

## 🔧 Konfiguracja

Do lokalnego uruchomienia potrzebujesz .NET 10 SDK oraz danych projektu Supabase Auth i połączenia z PostgreSQL. W katalogu repozytorium zapisz te wartości w .NET user-secrets pod kluczami:

| Klucz | Znaczenie |
| --- | --- |
| `Supabase:Url` | Adres projektu Supabase. |
| `Supabase:PublishableKey` | Klucz publikowalny projektu Supabase. |
| `Supabase:SecretKey` | Tajny klucz Supabase do generowania linków odzyskiwania hasła, tylko na backendzie. |
| `Mailtrap:ApiToken` | Token API Mailtrap Email Sending (Transactional). |
| `Mailtrap:FromEmail` | Adres nadawcy Mailtrap: testowo z domeny Demo, a do wysyłki do użytkowników z własnej zweryfikowanej domeny. |
| `Mailtrap:FromName` | Opcjonalna nazwa nadawcy; domyślnie `E-commerce`. |
| `PasswordReset:Url` | Bazowy adres linku resetu (HTTPS, bez fragmentu). Docelowo strona frontendu; przy ręcznej próbie może to być `https://localhost:3100/`, aby odczytać token z otrzymanego linku. |
| `ConnectionStrings:SupabaseDatabase` | Connection string PostgreSQL używany przez EF Core. |

Nie zapisuj rzeczywistych wartości ani hasła do bazy w repozytorium. Aplikacja wymaga tych ustawień przy starcie. Lokalny profil HTTPS używa certyfikatu deweloperskiego .NET. Dla lokalnego frontendu z HTTP potrzebny jest adres HTTPS (np. lokalny reverse proxy); endpoint resetu odrzuca URL bez HTTPS.

### Uruchomienie aplikacji

W katalogu repozytorium:

```powershell
# 1. Pobierz zależności
dotnet restore E-commerce-backend.slnx

# 2. Zapisz własne ustawienia lokalnie (zastąp wartości przykładowe)
dotnet user-secrets set "Supabase:Url" "https://TWOJ-PROJEKT.supabase.co"
dotnet user-secrets set "Supabase:PublishableKey" "TWOJ_KLUCZ_PUBLIKOWALNY"
dotnet user-secrets set "Supabase:SecretKey" "TWOJ_KLUCZ_TAJNY"
dotnet user-secrets set "Mailtrap:ApiToken" "TWOJ_TOKEN_MAILTRAP"
dotnet user-secrets set "Mailtrap:FromEmail" "sender@twoja-zweryfikowana-domena.pl"
dotnet user-secrets set "Mailtrap:FromName" "E-commerce"
dotnet user-secrets set "PasswordReset:Url" "https://twoj-frontend.pl/reset-password"
dotnet user-secrets set "ConnectionStrings:SupabaseDatabase" "TWOJ_CONNECTION_STRING_POSTGRESQL"

# 3. Uruchom API przez HTTPS
dotnet run --launch-profile https
```

Serwis `MailtrapEmailSender` korzysta z Transactional Email API przez `HttpClient`; nie wymaga zewnętrznego pakietu. Oficjalny [Mailtrap .NET SDK](https://github.com/mailtrap/mailtrap-dotnet) jest opcjonalną alternatywą. Pakiet jest publikowany w GitHub Packages i wymaga przy instalacji nazwy użytkownika GitHub oraz PAT z uprawnieniem `read:packages`; ten PAT jest odrębny od tokenu Mailtrap. Nie zapisuj żadnego z nich w repozytorium ani nie używaj opcji `--store-password-in-clear-text` bez świadomej zgody na jawny zapis poświadczenia w lokalnej konfiguracji NuGet.

Bez własnej domeny można użyć domeny Demo Mailtrap, np. `hello@demomailtrap.co` z przykładu integracji, lecz wtedy Mailtrap wyśle wiadomość tylko na adres użyty do założenia konta Mailtrap. Ten adres musi też należeć do istniejącego użytkownika Supabase, aby przetestować reset. Do wysyłki do dowolnych użytkowników trzeba dodać własną domenę i uzyskać status `Verified` w Mailtrap. Wartość `Mailtrap:FromEmail` powinna odpowiadać nadawcy wyświetlanemu w integracji tej domeny. Kroki żądania i potwierdzenia opisuje [kontrakt API](docs/api-contract.md#reset-hasła--próba-ręczna).

API będzie dostępne pod `https://localhost:3100/`; `GET /` zwraca `{"message":"API działa"}`. Profil `http` używa `http://localhost:5294`, lecz do prób logowania używaj HTTPS, ponieważ ciasteczko sesji ma flagę `Secure`.

W środowisku `Development` interaktywna dokumentacja API jest dostępna pod `https://localhost:3100/swagger`, a dokument OpenAPI pod `https://localhost:3100/openapi/v1.json`. Dokument powstaje z endpointów zarejestrowanych w uruchomionej aplikacji; po dodaniu trasy przebuduj i uruchom API ponownie, a następnie odśwież Swagger UI. Szczegółowe opisy i kody odpowiedzi wymagają odpowiednich metadanych na endpointach.

Przy próbach `PUT /api/v1/web/profile/me` i `POST /api/v1/web/auth/logout` w Swagger UI wpisz `XMLHttpRequest` w polu nagłówka `X-Requested-With`. Zapis profilu wymaga także sesji uzyskanej przez wcześniejsze logowanie przez HTTPS; wylogowanie zwraca `204` również bez istniejącej sesji.

Możesz też uruchomić backend przez Docker Compose. Wymaga to przygotowania `.env.local` i lokalnego certyfikatu; pełna instrukcja jest w [docs/docker.md](docs/docker.md).

### Skrypty i polecenia

| Polecenie | Opis |
| --- | --- |
| `dotnet restore E-commerce-backend.slnx` | Pobiera zależności rozwiązania. |
| `dotnet run --launch-profile https` | Uruchamia lokalne API przez HTTPS. |
| `dotnet build E-commerce-backend.slnx` | Kompiluje API i projekt testowy. |
| `dotnet test E-commerce-backend.slnx` | Uruchamia testy. |
| `docker compose up --build` | Buduje i uruchamia API w kontenerze po przygotowaniu środowiska. |

## Dokumentacja

- [Plan prac i postęp backendu](docs/project-direction.md) — moduły, kolejność, lista prac, statusy i decyzje do uzgodnienia.
- [Wstępny model katalogu](docs/catalog-model.md) — produkt, wariant, cechy, kategorie i planowane relacje.
- [Wstępny model magazynu](docs/inventory-model.md) — stan przypisany do wariantu i pytania przed checkoutem.
- [Wstępny model cen](docs/pricing-model.md) — cena wariantu, waluta i propozycja historii zmian.
- [Architektura i mapa kodu](docs/architecture.md) — punkt wejścia do obecnego backendu.
- [Kontrakt API](docs/api-contract.md) — aktualne trasy, żądania i odpowiedzi.
- [Baza danych i EF Core](docs/database.md) — schematy, migracje i granice dostępu.
- [Uwierzytelnianie: stan i plan nauki](docs/auth-implementation-plan.md) oraz [profile użytkowników](docs/user-profiles.md).
- [Testy](docs/testing.md), [Docker](docs/docker.md) i [obszary chronione](docs/protected-areas.md).

Kod i konfiguracja są źródłem prawdy o obecnym działaniu aplikacji. Instrukcje pracy dla asystentów AI znajdują się w [AGENTS.md](AGENTS.md).

## 📜 Licencja

Copyright (c) 2026 [Patryk Piotrowski](https://github.com/piotrowski-patryk) & [Piotr Ostrowski](https://github.com/PiotrO9). All rights reserved.

Warunki używania kodu określa plik [LICENSE](LICENSE).
