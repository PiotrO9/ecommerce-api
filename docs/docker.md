# Uruchamianie backendu przez Docker

Cel: opisać lokalny start API przez HTTPS na `https://localhost:3100` na Windows i macOS.

## Stan potwierdzony w kodzie

- `Dockerfile` buduje API na .NET 10 i uruchamia je jako użytkownik bez uprawnień roota.
- `compose.yaml` buduje ten obraz, mapuje port komputera `3100` na port HTTPS kontenera `8443`, montuje lokalny certyfikat tylko do odczytu i przekazuje ustawienia z `.env.local`.
- `compose.yaml` montuje własny wolumin Compose w `/app/keys`. Tam ASP.NET Core Data Protection zapisuje klucze chroniące ciasteczka logowania. Zwykły restart kontenera zachowuje klucze; `docker compose down -v` usuwa wolumin i unieważnia wcześniejsze ciasteczka. Dostęp do woluminu trzeba ograniczyć.
- `Program.cs` wymaga `Supabase:Url`, `Supabase:PublishableKey`, `Supabase:SecretKey`, `ConnectionStrings:SupabaseDatabase`, `Mailtrap:ApiToken`, `Mailtrap:FromEmail` i `PasswordReset:Url`. W kontenerze klucze konfiguracji zapisuje się ze znakami `__` zamiast `:`.
- `.env.example` zawiera tylko wzór. Prawdziwy `.env.local` i katalog `.local/` są wykluczone z Gita i kontekstu budowy obrazu.
- `GET /` zwraca JSON `{"message":"API działa"}`. `Properties/launchSettings.json` dotyczy `dotnet run`, nie kontenera.

## Wymagania

- Windows lub macOS z uruchomionym Docker Desktop i kontenerami Linux.
- Docker Compose **2.30.0 lub nowszy** (`docker compose version`). Konfiguracja używa `env_file` z `format: raw`, aby wartości z `$` nie były interpretowane przez Compose. [Dokumentacja Docker Compose](https://docs.docker.com/reference/compose-file/services/#env_file).
- .NET 10 SDK (`dotnet --version`) do jednorazowego przygotowania lokalnego certyfikatu HTTPS.
- Własne dane dostępu do projektu Supabase Auth i PostgreSQL. Połączenie z bazą musi działać z sieci komputera, na którym uruchamiasz Docker.

## Pierwsze przygotowanie: ustawienia

W katalogu projektu skopiuj `.env.example` do `.env.local`: na Windows w PowerShell `Copy-Item .env.example .env.local`, a na macOS `cp .env.example .env.local`. Następnie zastąp wszystkie wartości przykładowe:

- `Supabase__Url` — adres projektu Supabase.
- `Supabase__PublishableKey` — publishable key tego projektu.
- `Supabase__SecretKey` — tajny klucz używany przez backend przy resetowaniu hasła.
- `ConnectionStrings__SupabaseDatabase` — connection string PostgreSQL, w tym hasło bazy.
- `Mailtrap__ApiToken` — token Mailtrap Transactional Email API.
- `Mailtrap__FromEmail` — poprawny adres nadawcy wiadomości.
- `Mailtrap__FromName` — opcjonalna nazwa nadawcy.
- `PasswordReset__Url` — adres HTTPS strony resetu hasła, bez fragmentu URL.
- `ASPNETCORE_Kestrel__Certificates__Default__Password` — hasło, którego użyjesz przy eksporcie certyfikatu poniżej.

`.env.local` jest plikiem lokalnym zawierającym hasło do bazy. Nie dodawaj go do Gita, nie wysyłaj innym osobom i nie wklejaj do zgłoszeń. Zmienne środowiskowe są tu rozwiązaniem do lokalnego developmentu; przy wdrożeniu publicznym potrzebny będzie magazyn sekretów środowiska docelowego. Nie uruchamiaj `docker compose config` bez `--quiet`, bo może wypisać wartości zmiennych.

## Pierwsze przygotowanie: certyfikat localhost

Certyfikat zapisujemy w `.local/https/devcert.pfx`. W poniższych poleceniach zastąp `TWOJE_HASLO_CERTYFIKATU` **dokładnie tą samą wartością**, którą wpisałeś w `.env.local`.

Windows, PowerShell:

```powershell
New-Item -ItemType Directory -Force .local/https | Out-Null
dotnet dev-certs https -ep .local/https/devcert.pfx -p "TWOJE_HASLO_CERTYFIKATU"
dotnet dev-certs https --trust
```

macOS, Terminal:

```bash
mkdir -p .local/https
dotnet dev-certs https -ep .local/https/devcert.pfx -p 'TWOJE_HASLO_CERTYFIKATU'
dotnet dev-certs https --trust
```

System może poprosić o potwierdzenie zaufania certyfikatowi. Certyfikat deweloperski służy wyłącznie do lokalnego `localhost`; nie umieszczaj go w obrazie Docker. [Microsoft Learn — HTTPS w kontenerach ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/security/docker-https?view=aspnetcore-10.0).

## Uruchomienie i sprawdzenie

W katalogu projektu uruchom na Windows lub macOS:

```text
docker compose config --quiet
docker compose up --build
```

Otwórz `https://localhost:3100/`. Oczekiwana odpowiedź: `{"message":"API działa"}`. `Ctrl+C` zatrzyma kontener; `docker compose down` usunie kontener i sieć, ale zachowa wolumin kluczy. Po zalogowaniu sprawdź też `/api/v1/web/auth/me` i odczyt profilu, ponieważ samo `GET /` nie sprawdza połączenia z Supabase i PostgreSQL.

## Gdy start się nie udaje

- Brak `.env.local` lub certyfikatu: wykonaj oba kroki pierwszego przygotowania.
- Błąd połączenia z Docker Engine: uruchom Docker Desktop i poczekaj, aż będzie gotowy.
- Port `3100` jest zajęty: zatrzymaj aplikację, która go używa. Profil `https` w `dotnet run` również korzysta z portu `3100`.
- Błąd odczytu certyfikatu w kontenerze: sprawdź ścieżkę `.local/https/devcert.pfx`, zgodność hasła i uprawnienia odczytu pliku dla użytkownika aplikacji w kontenerze.
- Odpowiedź z `GET /` nie potwierdza dostępu do Supabase ani bazy; wykonaj żądania uwierzytelnienia i profilu.
- Warning o nadpisaniu `HTTP_PORTS` przez `URLS` jest oczekiwany: Compose uruchamia HTTPS zamiast domyślnego HTTP obrazu.
- Lokalny wolumin Data Protection przechowuje klucze bez dodatkowego szyfrowania na dysku. Publiczne wdrożenie wymaga osobnej konfiguracji ochrony kluczy i certyfikatu domeny.

## Weryfikacja na platformach

Konfiguracja Compose została przygotowana dla Windows i macOS. Działanie na każdym systemie należy potwierdzić przez start kontenera, żądania HTTP i dostęp do Supabase. Wynik testów na jednym systemie nie potwierdza automatycznie wyniku na drugim.
