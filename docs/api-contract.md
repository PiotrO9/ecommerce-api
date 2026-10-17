# Kontrakt obecnego API

Cel: opisać zewnętrzne zachowanie endpointów istniejących w kodzie. Źródłem prawdy są `Controllers/`, `Contracts/` i `Services/Auth/`; ten dokument trzeba aktualizować razem ze zmianą odpowiedzi API.

## Stan potwierdzony w kodzie

Wszystkie trasy kontrolerów zaczynają się od `/api/v1/web`. Pola JSON mają nazwy w stylu `camelCase`. Żądania `POST` i `PUT` z danymi przyjmują JSON. Błędy walidacji modeli wejściowych obsługuje także `[ApiController]`, więc ich treść może różnić się od komunikatów tworzonych jawnie w kontrolerach.

| Metoda i trasa | Żądanie | Sukces | Ważne błędy |
| --- | --- | --- | --- |
| `GET /` | Bez logowania | `200`, `{"message":"API działa"}` | — |
| `POST /api/v1/web/auth/register` | `email`, `password`, `confirmPassword` | `201`, bez sesji w odpowiedzi | `400` dla znanych błędów danych/hasła, `409` dla rozpoznanego istniejącego adresu, `429` dla limitu, `503` dla niedostępnej lub nieznanej odpowiedzi usługi |
| `POST /api/v1/web/auth/login` | `email`, `password`, opcjonalne `rememberMe` (domyślnie `false`) | `200` z pustym body i ciasteczkiem sesji | `401` dla rozpoznanych błędnych danych, `429` dla limitu, `503` dla niedostępnej lub nieznanej odpowiedzi usługi |
| `GET /api/v1/web/auth/me` | Ciasteczko sesji | `200`, `id`, `email`, `firstName`, `lastName` | `401` bez ważnej sesji, `503` gdy Supabase Auth lub baza jest niedostępna |
| `POST /api/v1/web/auth/logout` | Ciasteczko sesji i `X-Requested-With: XMLHttpRequest` | `204`; również gdy sesji już nie ma | `403` bez wymaganego nagłówka, `503` gdy wylogowanie jest niedostępne |
| `GET /api/v1/web/profile/me` | Ciasteczko sesji | `200`, `userId`, `firstName`, `lastName` | `401` bez ważnej sesji, `404` przed pierwszym zapisem profilu, `503` gdy usługa lub baza jest niedostępna |
| `PUT /api/v1/web/profile/me` | Ciasteczko sesji, `X-Requested-With: XMLHttpRequest`, JSON `firstName`, `lastName` | `200`, `userId`, `firstName`, `lastName` | `400` dla nieprawidłowych danych, `401` bez ważnej sesji, `403` bez wymaganego nagłówka, `503` gdy usługa lub baza jest niedostępna |

`RegisterRequest` wymaga prawidłowego formatu e-maila, hasła długości 6–50 znaków i zgodnego potwierdzenia. Dodatkowe wymagania kategorii znaków egzekwuje Supabase Auth. `LoginRequest` nie sprawdza polityki siły hasła. Profil wymaga niepustego imienia i nazwiska, każde do 100 znaków; przed zapisem API usuwa białe znaki z początku i końca.

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
- `POST /auth/logout` i `PUT /profile/me` wymagają nagłówka `X-Requested-With: XMLHttpRequest`; kontroler sprawdza go przed sesją. Jest to element obecnej ochrony żądań zmieniających dane, a nie dowód tożsamości użytkownika.
- Odpowiedzi zawierające dane sesji lub profilu mają `Cache-Control: no-store` w kontrolerach. Do lokalnych prób z ciasteczkiem `Secure` używaj HTTPS.

## Plany i sprawy otwarte

Endpointów logowania przez Google lub Apple nie ma jeszcze w kodzie. Kontrakt dla frontendu działającego na innej witrynie, w tym CORS, cookies i CSRF, nie został ustalony. Przed publikacją należy przetestować powyższe odpowiedzi z prawdziwym Supabase i PostgreSQL; testy lokalne korzystają także z atrap.
