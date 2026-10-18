# E-commerce backend

Backend e-commerce w ASP.NET Core na .NET 10. Supabase Auth obsługuje konta i sesje, a EF Core z Npgsql przechowuje dane aplikacji w PostgreSQL. Obecnie działają endpointy uwierzytelniania e-mail/hasło i profilu użytkownika; pozostałe funkcje sklepu są przyszłą pracą.

## Dokumentacja

- [Architektura i mapa kodu](docs/architecture.md) — od tego zacznij poznawanie projektu.
- [Kontrakt API](docs/api-contract.md) — obecne trasy, żądania i odpowiedzi.
- [Baza danych i EF Core](docs/database.md) — schematy, migracje i granice dostępu.
- [Uwierzytelnianie: stan i plan nauki](docs/auth-implementation-plan.md) oraz [profile użytkowników](docs/user-profiles.md).
- [Testy](docs/testing.md), [uruchamianie przez Docker](docs/docker.md) i [obszary chronione](docs/protected-areas.md).

Kod i konfiguracja są źródłem prawdy, gdy opis wymaga aktualizacji. Instrukcje pracy dla asystentów AI są w [AGENTS.md](AGENTS.md).

## Lokalny start

Przy lokalnym uruchomieniu przez `dotnet run` ustaw w .NET user-secrets klucze `Supabase:Url`, `Supabase:PublishableKey` i `ConnectionStrings:SupabaseDatabase`; wartości nie umieszczaj w repozytorium. `Properties/launchSettings.json` definiuje profile `http` (`http://localhost:5294`) i `https` (`https://localhost:3100`). HTTPS uruchomisz z katalogu projektu poleceniem `dotnet run --launch-profile https`.

Na Windows i macOS backend w kontenerze uruchomisz przez `docker compose up --build` po jednorazowym przygotowaniu `.env.local` i certyfikatu localhost. Szczegóły są w [docs/docker.md](docs/docker.md).

Kompilacja i testy: `dotnet build E-commerce-backend.slnx` oraz `dotnet test E-commerce-backend.slnx`. Działające API odpowiada na `GET /`.
