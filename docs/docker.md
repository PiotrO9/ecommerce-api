# Uruchamianie backendu przez Docker

Cel: opisać najprostszy lokalny start API przez HTTPS na porcie `3100`.

## Stan potwierdzony

- `Dockerfile` buduje projekt `E-commerce-backend.csproj` na .NET 10 i uruchamia go w obrazie ASP.NET Core 10.
- `start-docker.ps1` buduje obraz, uruchamia kontener HTTPS i mapuje port komputera `3100` na port kontenera `8443`.
- Skrypt montuje nazwany wolumin Docker `ecommerce-backend-keys` w `/app/keys`. Aplikacja zapisuje tam klucze ASP.NET Core Data Protection, aby po ponownym uruchomieniu kontenera móc odczytać wystawione wcześniej ciasteczka logowania. Usunięcie woluminu unieważni takie ciasteczka. Dostęp do woluminu trzeba ograniczyć, bo klucze chronią sesje użytkowników.
- Skrypt odczytuje z lokalnych .NET user-secrets `Supabase:Url`, `Supabase:PublishableKey` i `ConnectionStrings:SupabaseDatabase`, zapisuje je na czas działania w tymczasowym pliku `--env-file` poza repozytorium, a po zakończeniu usuwa plik. Przed uruchomieniem trzeba skonfigurować wszystkie trzy ustawienia. Skrypt nie wpisuje sekretów do obrazu Docker.
- Endpoint `GET /` zwraca JSON z komunikatem `API działa`.
- `Properties/launchSettings.json` służy uruchamianiu przez `dotnet run`; Docker nie korzysta z zawartych w nim portów.

## Wymagania

- Windows z PowerShell i uruchomionym Docker Desktop w trybie kontenerów Linux.
- .NET 10 SDK do przygotowania lokalnego certyfikatu HTTPS.

## Start po sklonowaniu repozytorium

W PowerShell przejdź do katalogu projektu i uruchom:

```powershell
.\start-docker.ps1
```

Przy pierwszym uruchomieniu system może poprosić o zaufanie certyfikatowi deweloperskiemu. Następnie otwórz `https://localhost:3100/`. Terminal pozostanie zajęty logami aplikacji; `Ctrl+C` zatrzyma kontener.

## Sprawdzenie działania

W drugim oknie PowerShell wyślij żądanie do API:

```powershell
Invoke-RestMethod https://localhost:3100/ | ConvertTo-Json -Compress
```

Oczekiwana odpowiedź: `{"message":"API działa"}`. Działający kontener możesz też zobaczyć poleceniem `docker ps`.

## Certyfikat

Skrypt tworzy certyfikat w katalogu użytkownika poza repozytorium, przekazuje go kontenerowi tylko do odczytu i nie zapisuje go w obrazie. Certyfikat deweloperski służy wyłącznie do lokalnego `localhost`; publiczne wdrożenie wymaga osobnej konfiguracji certyfikatu dla domeny. Źródło: [Microsoft Learn — HTTPS w kontenerach ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/security/docker-https?view=aspnetcore-10.0).

## Gdy start się nie udaje

- Jeśli Docker zgłasza błąd połączenia z silnikiem, uruchom Docker Desktop i poczekaj, aż będzie gotowy.
- Jeśli port `3100` jest zajęty, zatrzymaj aplikację, która go używa, przed ponownym uruchomieniem skryptu.
- Warning o nadpisaniu `HTTP_PORTS` przez `URLS` jest oczekiwany: skrypt uruchamia HTTPS zamiast domyślnego HTTP obrazu.
- Skrypt lokalny montuje klucze Data Protection bez dodatkowego szyfrowania kluczy na dysku. W środowisku produkcyjnym trzeba skonfigurować wspólny trwały magazyn kluczy oraz ich ochronę odpowiednią do platformy wdrożenia.
