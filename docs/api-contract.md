# Kontrakt obecnego API

Cel: opisać zewnętrzne zachowanie endpointów istniejących w kodzie. Źródłem prawdy są `Controllers/`, `Contracts/`, `Services/Auth/` i `Services/Email/`; ten dokument trzeba aktualizować razem ze zmianą odpowiedzi API.

## Stan potwierdzony w kodzie

Wszystkie trasy kontrolerów zaczynają się od `/api/v1/web`. Pola JSON mają nazwy w stylu `camelCase`. Żądania `POST` i `PUT` z danymi przyjmują JSON. Błędy walidacji modeli wejściowych obsługuje także `[ApiController]`, więc ich treść może różnić się od komunikatów tworzonych jawnie w kontrolerach.

| Metoda i trasa | Żądanie | Sukces | Ważne błędy |
| --- | --- | --- | --- |
| `GET /` | Bez logowania | `200`, `{"message":"API działa"}` | — |
| `GET /api/v1/web/categories` | Bez logowania; opcjonalne `rootId` (UUID) w query | `200`, tablica drzew kategorii | `400` dla niepoprawnego UUID, `404` dla nieistniejącego `rootId`, `503` przy niedostępnej bazie |
| `GET /api/v1/web/categories/{id}` | Bez logowania; `id` kategorii (UUID) w ścieżce | `200`, pojedyncza kategoria | `400` dla niepoprawnego UUID, `404` dla nieistniejącego `id`, `503` przy niedostępnej bazie |
| `GET /api/v1/web/products?id={uuid}` lub `?slug={slug}` | Bez logowania; dokładnie jeden parametr query: `id` (UUID) albo `slug` produktu | `200`, opublikowany produkt z kategoriami i widocznymi wariantami | `400` dla braku lub obu parametrów, pustego `slug` albo niepoprawnego UUID; `404` dla produktu nieistniejącego, ukrytego lub bez widocznego wariantu z ceną; `503` przy niedostępnej bazie |
| `GET /api/v1/web/products/filter-options` | Bez logowania; opcjonalne `categoryId` albo `slug` kategorii | `200`, tablica kluczy cech z nazwami i dostępnymi wartościami | `400` dla obu selektorów, pustego slugu lub błędnego UUID; `404` dla nieznanej kategorii; `409` dla niejednoznacznego slugu; `503` przy niedostępnej bazie |
| `GET /api/v1/web/products/catalog` | Bez logowania; opcjonalne `q`, `sort`, `minPrice`, `maxPrice`, `page` i `pageSize` | `200`, obiekt z lekkimi `items` oraz `pagination` | `400` dla błędnego `q`, `sort`, zakresu ceny, stronicowania lub dawnego filtra `attributes` w query; `503` przy niedostępnej bazie |
| `GET /api/v1/web/products/by-category?categoryId={uuid}` lub `?slug={slug}` | Bez logowania; dokładnie jeden parametr query: `categoryId` (UUID) albo `slug` kategorii; opcjonalne `q`, `sort`, `minPrice`, `maxPrice`, `page` i `pageSize` | `200`, obiekt z lekkimi `items` oraz `pagination` | `400` dla błędnego selektora kategorii, `q`, `sort`, zakresu ceny, stronicowania lub dawnego filtra `attributes` w query; `404` dla nieznanej kategorii; `409` dla niejednoznacznego slugu; `503` przy niedostępnej bazie |
| `POST /api/v1/web/products/search` | Bez logowania; JSON z opcjonalnymi `q`, `categoryId` albo `categorySlug`, `attributes`, `sort`, `minPrice`, `maxPrice`, `page` i `pageSize` | `200`, `items`, `pagination` i `availableFilters` z licznikami | `400` dla błędnego body, parametrów lub filtrów; `404` dla nieznanej kategorii; `409` dla niejednoznacznego slugu kategorii; `503` przy niedostępnej bazie |
| `POST /api/v1/web/auth/register` | `email`, `password`, `confirmPassword` | `201`, bez sesji w odpowiedzi | `400` dla znanych błędów danych/hasła, `409` dla rozpoznanego istniejącego adresu, `429` dla limitu, `503` dla niedostępnej lub nieznanej odpowiedzi usługi |
| `POST /api/v1/web/auth/login` | `email`, `password`, opcjonalne `rememberMe` (domyślnie `false`) | `200` z pustym body i ciasteczkiem sesji | `401` dla rozpoznanych błędnych danych, `429` dla limitu, `503` dla niedostępnej lub nieznanej odpowiedzi usługi |
| `POST /api/v1/web/auth/password-reset/request` | `email` | `202` z pustym body, niezależnie od istnienia konta i wyniku dostarczenia | `400` dla niepoprawnego formatu e-maila; `429` po przekroczeniu limitu żądań |
| `POST /api/v1/web/auth/password-reset/confirm` | `tokenHash`, `newPassword`, `confirmPassword` | `204` po zmianie hasła, bez zalogowania | `400` dla nieważnego linku, słabego hasła lub walidacji; `429` dla limitu; `503` dla niedostępności usługi |
| `GET /api/v1/web/auth/me` | Ciasteczko sesji | `200`, `id`, `email`, `firstName`, `lastName` | `401` bez ważnej sesji, `503` gdy Supabase Auth lub baza jest niedostępna |
| `POST /api/v1/web/auth/logout` | Opcjonalne ciasteczko sesji i `X-Requested-With: XMLHttpRequest` | `204`; również gdy sesji już nie ma | `403` bez wymaganego nagłówka, `503` gdy wylogowanie jest niedostępne |
| `GET /api/v1/web/profile/me` | Ciasteczko sesji | `200`, `userId`, `firstName`, `lastName` | `401` bez ważnej sesji, `404` przed pierwszym zapisem profilu, `503` gdy usługa lub baza jest niedostępna |
| `PUT /api/v1/web/profile/me` | Ciasteczko sesji, `X-Requested-With: XMLHttpRequest`, JSON `firstName`, `lastName` | `200`, `userId`, `firstName`, `lastName` | `400` dla nieprawidłowych danych, `401` bez ważnej sesji, `403` bez wymaganego nagłówka, `503` gdy usługa lub baza jest niedostępna |

Endpoint `POST /api/v1/web/seed` jest dostępny tylko w środowisku `Development` po ustawieniu `DemoSeed:Enabled=true` i klucza `DemoSeed:Key`. Wymaga nagłówka `X-Demo-Seed-Key`; poprawne wywołanie najpierw czyści wszystkie rekordy z dziewięciu tabel katalogu, stanów i historii cen, także spoza seedera, a następnie tworzy nowy zestaw. Nie dotyka kont, profili ani tabel `auth`. Reset i seed są jedną transakcją. Odpowiedź `200` zawiera `added` oraz `created` z licznikami nowych rekordów; błędny klucz daje `404`. Opcjonalne body JSON przyjmuje niezależne pola `categories`, `products`, `variants` z poziomami `low`, `med`/`medium`, `high` (domyślnie `medium` dla kategorii i produktów oraz `low` dla wariantów); każdy poziom losuje cel z opisanego w dokumentacji zakresu, a niepoprawny poziom daje `400`. Szczegóły zakresów i uruchomienia są w [demo-seed.md](demo-seed.md).

`GET /api/v1/web/categories` bez `rootId` zwraca wszystkie kategorie główne z potomkami; pusty katalog daje `[]`. Z `rootId` odpowiedź zawiera jedno drzewo, którego korzeniem jest wskazana kategoria, bez jej przodków i sąsiednich gałęzi. Każdy węzeł ma `id`, `parentId`, `displayName`, `slug` i tablicę `children`. Rodzeństwo jest obecnie sortowane alfabetycznie po `displayName`, z `id` jako rozstrzygnięciem remisu. Ten endpoint zwraca wyłącznie kategorie; produkty są dostępne przez `GET /api/v1/web/products/by-category`.

`GET /api/v1/web/categories/{id}` zwraca dane wskazanej kategorii: `id`, `displayName`, `slug`, `parentId`, `createdAt`, `updatedAt` oraz `path`. `path` zawiera elementy `{id, displayName, slug}` od kategorii głównej do wskazanej włącznie. Nie zawiera podkategorii ani produktów; do pobrania gałęzi służy `GET /api/v1/web/categories?rootId={id}`. Ponieważ `slug` jest unikalny tylko wśród rodzeństwa, odczyt pojedynczej kategorii używa UUID.

`GET /api/v1/web/products` wymaga dokładnie jednego z parametrów `id` i `slug`. Wartość `slug` jest przed wyszukaniem przycinana i zamieniana na małe litery. Odpowiedź zwraca `id`, `name`, `slug`, daty produktu, `categories` (`id`, `displayName`, `slug`) i `variants`. Wariant zawiera `id`, `sku`, opisy, `imageUrls`, daty, `attributes` (`code`, `displayName` klucza, `value`), `quantityOnHand` oraz `currentPrices` (`amount`, `currency`). Pole `quantityOnHand=null` oznacza brak rekordu stanu, a `0` oznacza znany zerowy stan. Endpoint pokazuje tylko produkt w statusie `Published` z przypisaną kategorią i opublikowanymi wariantami z obowiązującą ceną. W `currentPrices` jest ostatnia cena sprzedaży dla każdej waluty z `effectiveAt` nie późniejszym niż chwila odczytu; przy tej samej dacie skuteczności rozstrzygają `createdAt`, potem `id`. Nie zwraca historii cen, ceny przed obniżką ani oznaczenia promocji. Zasady promocji i informacji o wcześniejszej cenie pozostają osobnym zadaniem.

`GET /api/v1/web/products/catalog` zwraca wszystkie widoczne produkty, niezależnie od ich kategorii. Każdy produkt musi nadal mieć przynajmniej jedną przypisaną kategorię oraz opublikowany wariant z obowiązującą ceną. Zwraca `items` z lekkimi pozycjami listy oraz `pagination`; domyślna kolejność to nazwa produktu, potem ID. Pusty katalog daje `items: []`.

`GET /api/v1/web/products/by-category` zwraca obiekt z `items` i `pagination`. Elementy `items` mają ten sam lekki format co ogólny katalog. Lista obejmuje bezpośrednie przypisania do kategorii oraz wszystkie jej podkategorie; produkt przypisany do kilku pasujących kategorii pojawia się raz. Stosuje te same warunki widoczności produktu i wariantów co odczyt szczegółów. Istniejąca kategoria bez widocznych produktów daje `items: []`. Slug kategorii jest przycinany i zamieniany na małe litery, ale jest unikalny tylko wśród rodzeństwa. Jeśli pasuje do kilku kategorii w różnych gałęziach, endpoint zwraca `409`; wtedy trzeba wskazać `categoryId`.

Każda pozycja `items` w `catalog`, `by-category` i `search` ma `id`, `name`, `slug`, `thumbnailUrl` oraz `pricesFrom`. Miniatura to pierwsze zdjęcie pierwszego pasującego wariantu według SKU, który ma zdjęcie; gdy żaden go nie ma, wartość to `null`. `pricesFrom` zawiera najniższą bieżącą cenę sprzedaży **pasujących wariantów** osobno dla każdej waluty, jako `{amount, currency}`. Dla wariantu wybierana jest ostatnia obowiązująca zmiana ceny według `effectiveAt`, `createdAt`, `id`; następnie minimum w obrębie waluty. Przy filtrze cenowym `pricesFrom` zawiera wyłącznie PLN; bez niego może zawierać także inne waluty. Warunki `q` trafiającego w SKU, cech i ceny wpływają na to, które warianty budują kafelek. Bez takich warunków są to wszystkie widoczne, wycenione warianty produktu. Odpowiedź listy nie zawiera opisów, stanów ani historii cen; do ich pobrania służą szczegóły produktu. Zmiana formatu `items` względem wcześniejszego pełnego produktu wymaga aktualizacji klienta API.

`GET /api/v1/web/products/filter-options` bez parametrów zbiera cechy ze wszystkich widocznych produktów. `categoryId` lub `slug` ogranicza je do kategorii i jej potomków. Odpowiedź to tablica `{code, displayName, values}` uporządkowana po nazwie klucza i kodzie; wartości są unikalne i sortowane alfabetycznie. Cechy pochodzą wyłącznie z opublikowanych wariantów z obowiązującą ceną, należących do opublikowanych produktów z kategorią. Endpoint nie uwzględnia bieżących `q` ani wybranych filtrów; frontend może użyć jego `code` i `values` w `POST /products/search`.

Listy GET przyjmują opcjonalne `sort` w query, a `POST /products/search` — w body. Wartości: `name-asc` (domyślna), `name-desc`, `newest`, `oldest`, `price-asc`, `price-desc`. `newest` i `oldest` sortują po `publishedAt`; remisy rozstrzyga ID. Przy sortowaniu po cenie kluczem produktu jest najniższa bieżąca cena PLN spośród jego pasujących wariantów, także dla `price-desc`. Produkty bez bieżącej ceny PLN są na końcu w obu kierunkach; remisy rozstrzyga ID. Sortowanie odbywa się przed stronicowaniem. Nieznana wartość daje `400`.

Opcjonalne `minPrice` i `maxPrice` ograniczają bieżącą cenę sprzedaży wariantu w PLN, włącznie z podanymi granicami. Można podać tylko jedną granicę. Dozwolone są kwoty nieujemne z dokładnością do 0,01 PLN, mieszczące się w `numeric(18,2)` (`0`–`9999999999999999.99`); `minPrice` nie może przekraczać `maxPrice`. Błędny zakres daje `400`. Filtr ceny, cechy i trafienie `q` w SKU dotyczą **tego samego wariantu**. Historia cen i najniższa cena przed obniżką nie są używane jako filtr. Przykłady: `GET /api/v1/web/products/catalog?minPrice=50&maxPrice=150&sort=price-asc&page=1&pageSize=20` oraz `GET /api/v1/web/products/by-category?slug=demo-odziez&maxPrice=100&sort=price-desc`.

Opcjonalne `q` w obu listach szuka fragmentu nazwy produktu lub SKU opublikowanego wariantu z obowiązującą ceną. Wyszukiwanie nie rozróżnia wielkości liter; skrajne białe znaki są usuwane, a puste `q` działa jak brak filtra. Znaki `%`, `_` i `\` są traktowane dosłownie. Po przycięciu `q` może mieć maksymalnie 100 znaków; dłuższe daje `400`. Wyszukiwanie działa przed obliczeniem `totalItems` i wybraniem strony. Wyniki stosują wybrane `sort`, bez rankingu trafności. Przykłady: `GET /api/v1/web/products/catalog?q=koszulka` oraz `GET /api/v1/web/products/by-category?slug=demo-odziez&q=DEMO-TSHIRT`.

Zaawansowane wyszukiwanie używa `POST /api/v1/web/products/search` z body JSON. Jest to operacja wyłącznie odczytowa. Wszystkie pola są opcjonalne, ale body jest wymagane; `{}` zwraca pierwszą stronę widocznych produktów. Brakujące `page` i `pageSize` mają wartości `1` i `20`; `pageSize` musi mieścić się w zakresie 1–100. Można podać `categoryId` albo `categorySlug`, nigdy oba. Kategoria obejmuje potomków, tak samo jak w `GET /products/by-category`. `q` działa tak samo jak w listach GET.

```json
{
  "q": "koszulka",
  "categorySlug": "demo-odziez",
  "attributes": [
    { "code": "demo-kolor", "values": ["czerwony", "niebieski"] },
    { "code": "demo-rozmiar", "values": ["S"] }
  ],
  "minPrice": 50,
  "maxPrice": 150,
  "sort": "price-asc",
  "page": 1,
  "pageSize": 20
}
```

Filtr cech przyjmuje dowolne istniejące kody i wartości par, bez stałej listy kluczy. Można przekazać maksymalnie 10 obiektów `attributes` i łącznie 30 wartości. `code` i każdy element `values` są wymagane i przycinane z białych znaków na brzegach; długości po przycięciu to maksymalnie 100 znaków dla kodu i 255 dla wartości. Kod jest sprowadzany do małych liter; wartość jest porównywana dokładnie, z rozróżnieniem wielkości liter. Wiele wartości jednego kodu oznacza „dowolna z nich”, różne kody oznaczają „wszystkie”. Wszystkie warunki muszą pasować do jednego opublikowanego wariantu z obowiązującą ceną. Gdy `q` trafia w SKU i podano filtry, SKU oraz cechy muszą pochodzić z tego samego wariantu. Odpowiedź zawiera lekkie pozycje pasujących produktów. Nieznana para daje pustą listę. Filtry działają przed obliczeniem `totalItems` i wybraniem strony. Stary format `attributes[index]` w query endpointów GET jest odrzucany odpowiedzią `400`.

`availableFilters` w odpowiedzi `POST /products/search` jest tablicą `{code, displayName, values}`. Każdy element `values` ma `{value, count, selected}`. `count` oznacza liczbę **różnych produktów**, które można otrzymać po zastąpieniu wyboru danego klucza tą jedną wartością; produkt z kilkoma pasującymi wariantami liczy się raz. Przy liczeniu wartości jednego klucza stosuje się kategorię, `q`, zakres ceny i wszystkie filtry **innych** kluczy, pomijając obecny wybór tego klucza. Kilka wartości tego samego klucza w żądaniu nadal łączy się przez OR. Wszystkie warunki cech, ceny oraz trafienie `q` w SKU muszą dotyczyć tego samego widocznego, wycenionego wariantu. Liczniki obejmują cały wynik, niezależnie od `page`, `pageSize` i `sort`. Wartość wybrana w żądaniu zawsze pojawia się z `selected=true`, także gdy jej `count` wynosi `0`. Gdy kod cechy nie istnieje, `displayName` przyjmuje jego kod. Klucze bez żadnej dostępnej lub wybranej wartości są pomijane. Osobny `GET /products/filter-options` nadal podaje statyczny słownik dla katalogu lub kategorii, bez `q`, zakresu ceny i wybranych filtrów.

```json
{
  "items": [],
  "pagination": { "page": 1, "pageSize": 20, "totalItems": 0, "totalPages": 0,
    "hasPreviousPage": false, "hasNextPage": false },
  "availableFilters": [
    { "code": "demo-kolor", "displayName": "Kolor", "values": [
      { "value": "czerwony", "count": 0, "selected": true },
      { "value": "niebieski", "count": 1, "selected": false }
    ] }
  ]
}
```

Stronicowanie obu list produktów jest opcjonalne. Bez `page` i `pageSize` pole `items` zawiera wszystkie pasujące produkty, a `pagination` ma wartość `null`. Po podaniu któregokolwiek parametru brakujące wartości wynoszą `page=1` i `pageSize=20`. Strony liczymy od 1, a `pageSize` musi mieścić się w zakresie 1–100; strona po ostatniej daje `items: []`, o ile jej przesunięcie mieści się w zakresie typu `int`. Filtrowanie widoczności odbywa się przed liczeniem i wyborem strony. Pole `pagination` zawiera `page`, `pageSize`, `totalItems`, `totalPages`, `hasPreviousPage` i `hasNextPage`. Przykłady: `GET /api/v1/web/products/catalog?page=2&pageSize=20` oraz `GET /api/v1/web/products/by-category?slug=demo-odziez&page=2&pageSize=20`. Klienci powinni czytać produkty z `items`.

```json
{
  "items": [],
  "pagination": {
    "page": 14,
    "pageSize": 20,
    "totalItems": 251,
    "totalPages": 13,
    "hasPreviousPage": true,
    "hasNextPage": false
  }
}
```

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
