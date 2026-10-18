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
| `ConnectionStrings:SupabaseDatabase` | Connection string PostgreSQL używany przez EF Core. |

Nie zapisuj rzeczywistych wartości ani hasła do bazy w repozytorium. Aplikacja wymaga wszystkich trzech ustawień przy starcie. Lokalny profil HTTPS używa certyfikatu deweloperskiego .NET.

### Uruchomienie aplikacji

W katalogu repozytorium:

```powershell
# 1. Pobierz zależności
dotnet restore E-commerce-backend.slnx

# 2. Zapisz własne ustawienia lokalnie (zastąp wartości przykładowe)
dotnet user-secrets set "Supabase:Url" "https://TWOJ-PROJEKT.supabase.co"
dotnet user-secrets set "Supabase:PublishableKey" "TWOJ_KLUCZ_PUBLIKOWALNY"
dotnet user-secrets set "ConnectionStrings:SupabaseDatabase" "TWOJ_CONNECTION_STRING_POSTGRESQL"

# 3. Uruchom API przez HTTPS
dotnet run --launch-profile https
```

API będzie dostępne pod `https://localhost:3100/`; `GET /` zwraca `{"message":"API działa"}`. Profil `http` używa `http://localhost:5294`, lecz do prób logowania używaj HTTPS, ponieważ ciasteczko sesji ma flagę `Secure`.

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

- [Architektura i mapa kodu](docs/architecture.md) — punkt wejścia do obecnego backendu.
- [Kontrakt API](docs/api-contract.md) — aktualne trasy, żądania i odpowiedzi.
- [Baza danych i EF Core](docs/database.md) — schematy, migracje i granice dostępu.
- [Uwierzytelnianie: stan i plan nauki](docs/auth-implementation-plan.md) oraz [profile użytkowników](docs/user-profiles.md).
- [Testy](docs/testing.md), [Docker](docs/docker.md) i [obszary chronione](docs/protected-areas.md).

Kod i konfiguracja są źródłem prawdy o obecnym działaniu aplikacji. Instrukcje pracy dla asystentów AI znajdują się w [AGENTS.md](AGENTS.md).

## 📜 Licencja

Copyright (c) 2026 [Patryk Piotrowski](https://github.com/piotrowski-patryk) & [Piotr Ostrowski](https://github.com/PiotrO9). All rights reserved.

Warunki używania kodu określa plik [LICENSE](LICENSE).
