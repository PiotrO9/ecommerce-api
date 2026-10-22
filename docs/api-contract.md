# Kontrakt obecnego API

Cel: opisać zewnętrzne zachowanie endpointów istniejących w kodzie. Źródłem prawdy są `Controllers/`, `Contracts/`, `Services/Auth/` i `Services/Email/`; ten dokument trzeba aktualizować razem ze zmianą odpowiedzi API.

## Stan potwierdzony w kodzie

Wszystkie trasy kontrolerów zaczynają się od `/api/v1/web`. Pola JSON mają nazwy w stylu `camelCase`. Żądania `POST` i `PUT` z danymi przyjmują JSON. Błędy walidacji modeli wejściowych obsługuje także `[ApiController]`, więc ich treść może różnić się od komunikatów tworzonych jawnie w kontrolerach.

| Metoda i trasa | Żądanie | Sukces | Ważne błędy |
| --- | --- | --- | --- |
| `GET /` | Bez logowania | `200`, `{"message":"API działa"}` | — |
| `POST /api/v1/web/auth/register` | `email`, `password`, `confirmPassword` | `201`, bez sesji w odpowiedzi | `400` dla znanych błędów danych/hasła, `409` dla rozpoznanego istniejącego adresu, `429` dla limitu, `503` dla niedostępnej lub nieznanej odpowiedzi usługi |
| `POST /api/v1/web/auth/login` | `email`, `password`, opcjonalne `rememberMe` (domyślnie `false`) | `200` z pustym body i ciasteczkiem sesji | `401` dla rozpoznanych błędnych danych, `429` dla limitu, `503` dla niedostępnej lub nieznanej odpowiedzi usługi |
| `POST /api/v1/web/auth/password-reset/request` | `email` | `202` z pustym body, niezależnie od istnienia konta i wyniku dostarczenia | `400` dla niepoprawnego formatu e-maila; `429` po przekroczeniu limitu żądań |
| `POST /api/v1/web/auth/password-reset/confirm` | `tokenHash`, `newPassword`, `confirmPassword` | `204` po zmianie hasła, bez zalogowania | `400` dla nieważnego linku, słabego hasła lub walidacji; `429` dla limitu; `503` dla niedostępności usługi |
| `GET /api/v1/web/auth/me` | Ciasteczko sesji | `200`, `id`, `email`, `firstName`, `lastName` | `401` bez ważnej sesji, `503` gdy Supabase Auth lub baza jest niedostępna |
| `POST /api/v1/web/auth/logout` | Opcjonalne ciasteczko sesji i `X-Requested-With: XMLHttpRequest` | `204`; również gdy sesji już nie ma | `403` bez wymaganego nagłówka, `503` gdy wylogowanie jest niedostępne |
| `GET /api/v1/web/profile/me` | Ciasteczko sesji | `200`, `userId`, `firstName`, `lastName` | `401` bez ważnej sesji, `404` przed pierwszym zapisem profilu, `503` gdy usługa lub baza jest niedostępna |
| `PUT /api/v1/web/profile/me` | Ciasteczko sesji, `X-Requested-With: XMLHttpRequest`, JSON `firstName`, `lastName` | `200`, `userId`, `firstName`, `lastName` | `400` dla nieprawidłowych danych, `401` bez ważnej sesji, `403` bez wymaganego nagłówka, `503` gdy usługa lub baza jest niedostępna |

Endpoint `POST /api/v1/web/seed` jest dostępny tylko w środowisku `Development` po ustawieniu `DemoSeed:Enabled=true` i klucza `DemoSeed:Key`. Wymaga nagłówka `X-Demo-Seed-Key`; poprawne wywołanie najpierw czyści wszystkie rekordy z dziewięciu tabel katalogu, stanów i historii cen, także spoza seedera, a następnie tworzy nowy zestaw. Nie dotyka kont, profili ani tabel `auth`. Reset i seed są jedną transakcją. Odpowiedź `200` zawiera `added` oraz `created` z licznikami nowych rekordów; błędny klucz daje `404`. Opcjonalne body JSON przyjmuje niezależne pola `categories`, `products`, `variants` z poziomami `low`, `med`/`medium`, `high` (domyślnie `medium` dla kategorii i produktów oraz `low` dla wariantów); każdy poziom losuje cel z opisanego w dokumentacji zakresu, a niepoprawny poziom daje `400`. Szczegóły zakresów i uruchomienia są w [demo-seed.md](demo-seed.md).

`RegisterRequest` wymaga prawidłowego formatu e-maila, hasła długości 6–50 znaków i zgodnego potwierdzenia. Dodatkowe wymagania kategorii znaków egzekwuje Supabase Auth. `LoginRequest` nie sprawdza polityki siły hasła. Profil wymaga niepustego imienia i nazwiska, każde do 100 znaków; przed zapisem API usuwa białe znaki z początku i końca.

Żądanie resetu generuje token w Supabase Auth i wysyła link przez Mailtrap. Link prowadzi pod `PasswordReset:Url` i niesie `token_hash` w fragmencie URL. Frontend odczytuje fragment i przesyła token w JSON do `/password-reset/confirm` razem z nowym hasłem. Token jest jednorazowy; przy odrzuceniu nowego hasła przez politykę Supabase należy poprosić o kolejny link. Endpoint nie tworzy sesji ani ciasteczka; po sukcesie użytkownik loguje się nowym hasłem. `202` oznacza przyjęcie żądania przez API; jest zwracane także, gdy konto nie istnieje, Supabase nie wygenerował linku albo Mailtrap nie przyjął wiadomości. Nie potwierdza wysłania ani dostarczenia e-maila. Błędy usług są logowane bez adresu, tokenu i hasła.
Oba endpointy mają limit według adresu IP: 5 żądań na minutę dla prośby o link i 10 dla potwierdzenia. Przy wdrożeniu za reverse proxy należy skonfigurować zaufane nagłówki przekazywania adresu klienta.

### Reset hasła — próba ręczna

1. Przygotuj konto w Supabase Auth z adresem, na który Mailtrap może wysłać wiadomość. Przy nadawcy z domeny Demo może to być tylko adres konta Mailtrap; dla innych odbiorców potrzebna jest zweryfikowana domena nadawcy.
2. Wyślij `POST /api/v1/web/auth/password-reset/request` z nagłówkiem `Content-Type: application/json` i body `{"email":"twoj-adres@example.com"}`. Logowanie i ciasteczko sesji nie są wymagane. Odpowiedź `202` ma puste body.
3. Sprawdź [Mailtrap Email Logs](https://mailtrap.io/sending/email_logs) oraz skrzynkę odbiorczą. Jeśli nie ma wpisu w Mailtrap, sprawdź logi API: żądanie może zwrócić `202` także przy błędzie Supabase lub Mailtrap. Sam status HTTP Mailtrap potwierdza przyjęcie żądania przez jego API, nie dostarczenie do skrzynki.
4. Otwórz otrzymany link. Przy docelowej stronie frontend odczytuje `token_hash` z części po `#`. Na czas ręcznej próby `PasswordReset:Url` może wskazywać na `https://localhost:3100/`; otwarcie linku pokaże stronę API, a token pozostanie w pasku adresu po `#token_hash=`. Skopiuj samą wartość tokenu; nie umieszczaj jej w logach, zgłoszeniu ani repozytorium.
5. Wyślij `POST /api/v1/web/auth/password-reset/confirm` z nagłówkiem `Content-Type: application/json` i poniższym JSON. Podstaw skopiowany token i nowe hasło spełniające politykę Supabase. Sukces to `204` z pustym body; potem zaloguj się nowym hasłem.

```json
{
  "tokenHash": "TOKEN_Z_LINKU",
  "newPassword": "NOWE_HASLO",
  "confirmPassword": "NOWE_HASLO"
}
```

Przykład odpowiedzi `/api/v1/web/auth/me` przed pierwszym zapisem profilu:

```json
{
  "id": "00000000-0000-0000-0000-000000000001",
  "email": "user@example.com",
  "firstName": null,
  "lastName": null
}
```

`GET /profile/me` w tej samej sytuacji zwraca `404`. Profil powstaje dopiero przy pierwszym `PUT /profile/me`, nie przy rejestracji konta. Klient nie przekazuje `userId` przy zapisie: backend bierze go ze zweryfikowanej sesji.

## Sesja i nagłówki

- `POST /auth/login` ustawia jedno ciasteczko `__Host-ecomm-auth` z flagami `HttpOnly`, `Secure`, `SameSite=Lax` i `Path=/`. Zawiera chronioną parę tokenów Supabase oraz termin sesji; API nie zwraca tokenów w JSON.
- Ciasteczko jest trwałe do ustalonego terminu: 1 dzień przy `rememberMe=false` albo 30 dni przy `rememberMe=true`. Odświeżanie tokenów nie przesuwa tego terminu.
- `POST /auth/logout` i `PUT /profile/me` wymagają nagłówka `X-Requested-With: XMLHttpRequest`; parametry `[FromHeader]` udostępniają pole w Swagger UI, a kontrolery sprawdzają wartość przed sesją. Jest to element obecnej ochrony żądań zmieniających dane, a nie dowód tożsamości użytkownika.
- Odpowiedzi zawierające dane sesji lub profilu mają `Cache-Control: no-store` w kontrolerach. Do lokalnych prób z ciasteczkiem `Secure` używaj HTTPS.

## Plany i sprawy otwarte

Endpointów logowania przez Google lub Apple nie ma jeszcze w kodzie. Kontrakt dla frontendu działającego na innej witrynie, w tym CORS, cookies i CSRF, nie został ustalony. Przed publikacją należy przetestować powyższe odpowiedzi z prawdziwym Supabase i PostgreSQL; testy lokalne korzystają także z atrap.
