# Dostęp administratora i zarządzanie cenami

Cel: opisać wdrożoną kontrolę dostępu do administracyjnych endpointów cen oraz sposób nadania pierwszego uprawnienia. Stan kodu: 31.10.2026. Reguły zapisu ceny opisuje [model cen](pricing-model.md), a format żądań [kontrakt API](api-contract.md).

## Model dostępu

`app.administrators` ma `user_id uuid` jako PK i FK do `auth.users.id` z `ON DELETE CASCADE` oraz `created_at` UTC. Istnienie wpisu oznacza uprawnienie administratora. Nie jest wymagany uzupełniony profil. `AdministratorAccess` najpierw sprawdza chronione ciasteczko przez istniejący `AuthenticatedUserService` i Supabase Auth, a następnie za każdym żądaniem sprawdza wpis dla potwierdzonego UUID. Nie korzysta z e-maila ani `user_metadata`. Usunięcie wpisu działa od następnego sprawdzenia, bez czekania na wygaśnięcie tokenu. Żądanie już autoryzowane może zakończyć rozpoczętą operację.

Migracja `20261031110337_AddAdministrators` tworzy tylko tabelę w `app`, włącza RLS i odbiera dostęp `PUBLIC`, `anon`, `authenticated`, `service_role`. Nie zmienia obiektów schematu `auth`. Backend używa dotychczasowego uprzywilejowanego połączenia EF. Nie istnieje publiczny endpoint nadawania uprawnienia ani mechanizm automatycznego awansu pierwszego konta.

Migracje `AddPriceChangeOperations` i `AddAdministrators` zastosowano w skonfigurowanej bazie Supabase 31.10.2026. Migracja nie awansowała żadnego konta; pierwszy wpis należy dodać dla wybranego istniejącego UUID.

## Nadanie pierwszego uprawnienia

1. Zarejestruj i potwierdź konto w zwykłym przepływie Supabase Auth. Odczytaj jego UUID w panelu Supabase Authentication → Users.
2. Zastosuj migracje aplikacji (`dotnet ef database update` z prawidłową konfiguracją połączenia).
3. Jako właściciel bazy uruchom w SQL Editor poniższe zapytanie, podstawiając UUID istniejącego konta:

```sql
INSERT INTO app.administrators (user_id)
SELECT id FROM auth.users WHERE id = '<UUID_KONTA>'::uuid
ON CONFLICT (user_id) DO NOTHING;
```

Jeśli UUID nie wskazuje istniejącego konta, zapytanie nie doda wpisu. Sprawdź wynik:

```sql
SELECT user_id, created_at FROM app.administrators
WHERE user_id = '<UUID_KONTA>'::uuid;
```

Odebranie uprawnienia:

```sql
DELETE FROM app.administrators WHERE user_id = '<UUID_KONTA>'::uuid;
```

Żadne konkretne konto nie jest awansowane przez migrację. Seeder nie tworzy ani nie usuwa administratorów: to uprawnienia rzeczywistych kont, a nie dane katalogu. Ich pominięcie w seederze i jego licznikach jest świadomym wyjątkiem zgodnym z [zasadami utrzymania](seeder-maintenance.md). Test integracyjny potwierdza zachowanie wpisu przy resecie katalogu.

## Użycie API

Zaloguj się przez `POST /api/v1/web/auth/login` i zachowaj otrzymane ciasteczko. Z tym ciasteczkiem można wywołać:

- `GET /api/v1/web/admin/variants/{id}/price` — aktualna cena PLN, również wariantu w szkicu lub ukrytego.
- `POST /api/v1/web/admin/variants/{id}/price-changes` — zapis zwykłej ceny PLN. Wymaga `X-Requested-With: XMLHttpRequest` i `Idempotency-Key` z nowym niepustym UUID dla nowej operacji.
- `GET /api/v1/web/admin/variants/{id}/price-changes?page=1&pageSize=20` — historia PLN z opcjonalnymi `from` i `to`.

Przykładowy JSON zmiany ceny:

```json
{
  "baseAmount": 79.99,
  "currency": "PLN",
  "reason": "Aktualizacja cennika",
  "expectedPriceChangeId": "d620ea8c-20fe-43d4-82af-20e4c401cb25"
}
```

Id poprzedniej ceny pochodzi z `price.id` odpowiedzi GET. Dla pierwszej ceny podaj null. Pominięte `currency` oznacza PLN. `baseAmount` i powód są wymagane; nie są przyjmowane pola autora, ceny sprzedaży i daty. Nowa operacja daje `201`, a ponowienie z tym samym kluczem i treścią daje `200` z oryginalnym wpisem i `replayed=true`. Przed ponowieniem nadal sprawdzana jest aktualna sesja i uprawnienie. Konflikt wersji wymaga ponownego odczytu ceny i nowego klucza dla zmienionego żądania.

GET bieżącej ceny zwraca obiekt `{price}`. `variantId` i `currency` występują tylko w zagnieżdżonej cenie; nie są powtarzane na zewnętrznym poziomie. Brak bieżącej ceny PLN to `{ "price": null }`.

W Postmanie albo Swaggerze korzystaj z tego samego hosta i HTTPS, aby przeglądarka/klient przesłały ciasteczko `Secure`. Swagger działa w Development pod `/swagger`. Odpowiedzi administracyjne mają `Cache-Control: no-store`. Brak ważnej sesji daje `401`, brak uprawnienia `403`, błędy danych `400`, brak wariantu `404`, konflikty `409`, a niedostępność Auth/bazy `503`. Trasy z niepoprawnym UUID nie pasują do routingu i dają `404`.

## Granice wdrożenia

Endpoint zmiany przyjmuje kwoty od 0 z dokładnością do grosza, zgodnie z serwisem i bazą. Na tym etapie nie wykonuje obliczeń VAT ani rabatów. Zmiana ceny podstawowej nie dodaje publicznego oznaczenia promocji. Istniejący rabat i przyszły wpis ceny blokują zwykły zapis z `409`; wymagają przyszłego modelu promocji/harmonogramu. Żaden endpoint tego modułu nie zmienia statusu produktu ani wariantu.

Testy używają lokalnego PostgreSQL i atrap odpowiedzi Auth. Potwierdzają sprawdzanie potwierdzonego ID, brak zaufania do `user_metadata`, odebranie uprawnienia, FK usuwania konta oraz zapis, ponowienie, historię i odczyt publiczny po zmianie. Próba z dwoma prawdziwymi kontami Supabase pozostaje do wykonania.
