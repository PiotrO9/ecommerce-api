# Kontrakt obecnego API

Cel: opisać zewnętrzne zachowanie endpointów istniejących w kodzie. Źródłem prawdy są `Controllers/`, `Contracts/`, `Services/Auth/` i `Services/Email/`; ten dokument trzeba aktualizować razem ze zmianą odpowiedzi API.

## Stan potwierdzony w kodzie

Wszystkie trasy kontrolerów zaczynają się od `/api/v1/web`. Pola JSON mają nazwy w stylu `camelCase`. Żądania `POST` i `PUT` z danymi przyjmują JSON. Błędy walidacji modeli wejściowych obsługuje także `[ApiController]`, więc ich treść może różnić się od komunikatów tworzonych jawnie w kontrolerach.

## Administracyjne ceny wariantu

Trzy trasy wymagają ważnej sesji i aktualnego wpisu `app.administrators`. Sprawdzenie poprzedza odczyt/zapis cen; wszystkie odpowiedzi kontrolera mają `Cache-Control: no-store`. Nadanie uprawnienia opisuje [dostęp administratora](administrator-access.md).

| Metoda i trasa | Żądanie | Sukces |
| --- | --- | --- |
| `GET /api/v1/web/admin/variants/{id}/price` | UUID wariantu w ścieżce | `200`, `{price}`; `variantId` i `currency` są tylko wewnątrz `price`; `price=null`, jeśli wariant istnieje, ale nie ma bieżącej ceny PLN |
| `POST /api/v1/web/admin/variants/{id}/price-changes` | JSON `baseAmount`, `reason`, opcjonalne `currency` (PLN) i `expectedPriceChangeId`; nagłówki `Idempotency-Key` (niepusty UUID) i `X-Requested-With: XMLHttpRequest` | `201` nowa operacja lub `200` ponowienie, `{replayed, price}` |
| `GET /api/v1/web/admin/variants/{id}/price-changes` | Opcjonalne `page` (1), `pageSize` (20, zakres 1–100), `from`, `to` (daty z offsetem) | `200`, `{items, page, pageSize, totalItems, totalPages}` |

Obiekt `price` i elementy historii mają `id`, `variantId`, `baseAmount`, `sellingAmount`, `currency`, `effectiveAt`, `createdAt`, `reason`, `previousPriceChangeId`, `changedByUserId`. Daty są w UTC; stare/demonstracyjne wpisy mogą mieć autora i poprzednika null. Historia obejmuje także przyszłe rekordy PLN; `from` jest włączne, `to` wyłączne, według `effectiveAt`. GET ceny bieżącej pomija przyszłe wpisy. Sortowanie historii: `effectiveAt DESC`, `createdAt DESC`, `id DESC`. Wariant bez ceny ma pustą historię, a nieistniejący daje `404`.

POST przyjmuje kwoty `0`–`9999999999999999.99`, maksymalnie dwa miejsca po przecinku, przycięty powód 1–120 znaków. Przy pierwszej cenie `expectedPriceChangeId` jest null; przy kolejnej musi wskazywać ostatnią cenę PLN. Backend ustala czas, autora i cenę sprzedaży. Nieznane pola JSON są odrzucane (`400`), więc body nie może narzucić autora, daty ani ceny sprzedaży. Cena sprzedaży bez promocji jest równa podstawowej.

Błędy: `401` bez sesji; `403` bez uprawnienia lub wymaganego nagłówka CSRF; `400` dla danych/body/nagłówka klucza/paginacji; `404` dla wariantu lub niepasującego do trasy UUID; `503` przy niedostępności Auth/bazy. Konflikty `409` mają `code`: `price_version_conflict`, `idempotency_key_conflict`, `discounted_price_conflict`, `future_price_conflict`, `price_timestamp_conflict`. Powtórzenie udanej operacji z tym samym autorem, kluczem i treścią zwraca jej oryginalny wpis także po późniejszych zmianach. Klucz z inną treścią daje konflikt.

## Pozostałe trasy

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

Endpoint `POST /api/v1/web/seed` jest dostępny tylko w środowisku `Development` po ustawieniu `DemoSeed:Enabled=true` i klucza `DemoSeed:Key`. Wymaga nagłówka `X-Demo-Seed-Key`; poprawne wywołanie najpierw czyści wszystkie rekordy z siedemnastu tabel katalogu, promocji i oferty, stanów i historii cen, także spoza seedera, a następnie tworzy nowy zestaw. Nie dotyka kont, profili ani tabel `auth`. Reset i seed są jedną transakcją. Odpowiedź `200` zawiera `added` oraz `created` z licznikami nowych rekordów; błędny klucz daje `404`. Opcjonalne body JSON przyjmuje niezależne pola `categories`, `products`, `variants` z poziomami `low`, `med`/`medium`, `high` (domyślnie `medium` dla kategorii i produktów oraz `low` dla wariantów); każdy poziom losuje cel z opisanego w dokumentacji zakresu, a niepoprawny poziom daje `400`. Szczegóły zakresów i uruchomienia są w [demo-seed.md](demo-seed.md).

`GET /api/v1/web/categories` bez `rootId` zwraca wszystkie kategorie główne z potomkami; pusty katalog daje `[]`. Z `rootId` odpowiedź zawiera jedno drzewo, którego korzeniem jest wskazana kategoria, bez jej przodków i sąsiednich gałęzi. Każdy węzeł ma `id`, `parentId`, `displayName`, `slug` i tablicę `children`. Rodzeństwo jest obecnie sortowane alfabetycznie po `displayName`, z `id` jako rozstrzygnięciem remisu. Ten endpoint zwraca wyłącznie kategorie; produkty są dostępne przez `GET /api/v1/web/products/by-category`.

`GET /api/v1/web/categories/{id}` zwraca dane wskazanej kategorii: `id`, `displayName`, `slug`, `parentId`, `createdAt`, `updatedAt` oraz `path`. `path` zawiera elementy `{id, displayName, slug}` od kategorii głównej do wskazanej włącznie. Nie zawiera podkategorii ani produktów; do pobrania gałęzi służy `GET /api/v1/web/categories?rootId={id}`. Ponieważ `slug` jest unikalny tylko wśród rodzeństwa, odczyt pojedynczej kategorii używa UUID.

`GET /api/v1/web/products` wymaga dokładnie jednego z parametrów `id` i `slug`. Wartość `slug` jest przed wyszukaniem przycinana i zamieniana na małe litery. Odpowiedź zwraca `id`, `name`, `slug`, daty produktu, `categories` (`id`, `displayName`, `slug`) i `variants`. Wariant zawiera `id`, `sku`, opisy, `imageUrls`, daty, `attributes` (`code`, `displayName` klucza, `value`), `quantityOnHand` oraz `currentPrices` (`amount`, `currency`, `baseAmount`, `variantId`, `reduction`). Pole `quantityOnHand=null` oznacza brak rekordu stanu, a `0` oznacza znany zerowy stan. Endpoint pokazuje tylko produkt w statusie `Published` z przypisaną kategorią i opublikowanymi wariantami z obowiązującą ceną. W `currentPrices` jest ostatnia cena sprzedaży dla każdej waluty z `effectiveAt` nie późniejszym niż chwila odczytu; przy tej samej dacie skuteczności rozstrzygają `createdAt`, potem `id`. Nie zwraca całej historii ani audytu administratora. Obowiązująca promocja ma zapisane odniesienie w `reduction`; kontrakt i przykład są poniżej.

`GET /api/v1/web/products/catalog` zwraca wszystkie widoczne produkty, niezależnie od ich kategorii. Każdy produkt musi nadal mieć przynajmniej jedną przypisaną kategorię oraz opublikowany wariant z obowiązującą ceną. Zwraca `items` z lekkimi pozycjami listy oraz `pagination`; domyślna kolejność to nazwa produktu, potem ID. Pusty katalog daje `items: []`.

`GET /api/v1/web/products/by-category` zwraca obiekt z `items` i `pagination`. Elementy `items` mają ten sam lekki format co ogólny katalog. Lista obejmuje bezpośrednie przypisania do kategorii oraz wszystkie jej podkategorie; produkt przypisany do kilku pasujących kategorii pojawia się raz. Stosuje te same warunki widoczności produktu i wariantów co odczyt szczegółów. Istniejąca kategoria bez widocznych produktów daje `items: []`. Slug kategorii jest przycinany i zamieniany na małe litery, ale jest unikalny tylko wśród rodzeństwa. Jeśli pasuje do kilku kategorii w różnych gałęziach, endpoint zwraca `409`; wtedy trzeba wskazać `categoryId`.

Każda pozycja `items` w `catalog`, `by-category` i `search` ma `id`, `name`, `slug`, `thumbnailUrl` oraz `pricesFrom`. Miniatura to pierwsze zdjęcie pierwszego pasującego wariantu według SKU, który ma zdjęcie; gdy żaden go nie ma, wartość to `null`. `pricesFrom` zawiera najniższą bieżącą cenę sprzedaży **pasujących wariantów** osobno dla każdej waluty, jako `{amount, currency, baseAmount, variantId, reduction}`. Dla wariantu wybierana jest ostatnia obowiązująca zmiana ceny według `effectiveAt`, `createdAt`, `id`; następnie minimum w obrębie waluty. Przy filtrze cenowym `pricesFrom` zawiera wyłącznie PLN; bez niego może zawierać także inne waluty. Warunki `q` trafiającego w SKU, cech i ceny wpływają na to, które warianty budują kafelek. Bez takich warunków są to wszystkie widoczne, wycenione warianty produktu. Odpowiedź listy nie zawiera opisów, stanów ani historii cen; do ich pobrania służą szczegóły produktu. Zmiana formatu `items` względem wcześniejszego pełnego produktu wymaga aktualizacji klienta API.

`GET /api/v1/web/products/filter-options` bez parametrów zbiera cechy ze wszystkich widocznych produktów. `categoryId` lub `slug` ogranicza je do kategorii i jej potomków. Odpowiedź to tablica `{code, displayName, values}` uporządkowana po nazwie klucza i kodzie; wartości są unikalne i sortowane alfabetycznie. Cechy pochodzą wyłącznie z opublikowanych wariantów z obowiązującą ceną, należących do opublikowanych produktów z kategorią. Endpoint nie uwzględnia bieżących `q` ani wybranych filtrów; frontend może użyć jego `code` i `values` w `POST /products/search`.

Listy GET przyjmują opcjonalne `sort` w query, a `POST /products/search` — w body. Wartości: `name-asc`, `name-desc`, `newest`, `oldest`, `price-asc`, `price-desc`. Bez `sort` i bez niepustego `q` domyślne jest `name-asc`; z niepustym `q` domyślnie stosowany jest ranking trafności opisany niżej. Jawne `sort` ma pierwszeństwo przed rankingiem. `newest` i `oldest` sortują po `publishedAt`; remisy rozstrzyga ID. Przy sortowaniu po cenie kluczem produktu jest najniższa bieżąca cena PLN spośród jego pasujących wariantów, także dla `price-desc`. Produkty bez bieżącej ceny PLN są na końcu w obu kierunkach; remisy rozstrzyga ID. Sortowanie odbywa się przed stronicowaniem. Nieznana wartość daje `400`.

Opcjonalne `minPrice` i `maxPrice` ograniczają bieżącą cenę sprzedaży wariantu w PLN, włącznie z podanymi granicami. Można podać tylko jedną granicę. Dozwolone są kwoty nieujemne z dokładnością do 0,01 PLN, mieszczące się w `numeric(18,2)` (`0`–`9999999999999999.99`); `minPrice` nie może przekraczać `maxPrice`. Błędny zakres daje `400`. Filtr ceny, cechy i trafienie `q` w SKU dotyczą **tego samego wariantu**. Historia cen i najniższa cena przed obniżką nie są używane jako filtr. Przykłady: `GET /api/v1/web/products/catalog?minPrice=50&maxPrice=150&sort=price-asc&page=1&pageSize=20` oraz `GET /api/v1/web/products/by-category?slug=demo-odziez&maxPrice=100&sort=price-desc`.

Opcjonalne `q` w obu listach oraz `POST /products/search` szuka fragmentu nazwy produktu lub SKU opublikowanego wariantu z obowiązującą ceną. Wyszukiwanie nie rozróżnia wielkości liter; skrajne białe znaki są usuwane, a puste `q` działa jak brak filtra. Znaki `%`, `_` i `\` są traktowane dosłownie. Po przycięciu `q` może mieć maksymalnie 100 znaków; dłuższe daje `400`. Wyszukiwanie działa przed obliczeniem `totalItems` i wybraniem strony. Bez jawnego `sort` kolejność to: dokładna nazwa produktu, dokładne SKU, początek nazwy, początek SKU, fragment nazwy, fragment SKU. Produkt otrzymuje najlepszą pozycję spośród wariantów, które przeszły wszystkie filtry cech i ceny. W tym samym poziomie rankingu remisy rozstrzyga nazwa, a potem ID produktu. Przykłady: `GET /api/v1/web/products/catalog?q=koszulka` oraz `GET /api/v1/web/products/by-category?slug=demo-odziez&q=DEMO-TSHIRT`.

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

## Promocje produktu

Stan na 31.10.2026: siedem tras pod `/api/v1/web/admin/promotions`, z tą samą sesją i weryfikacją administratora co ceny wariantu. Wszystkie odpowiedzi kontrolera mają `Cache-Control: no-store`. Mutacje wymagają `X-Requested-With: XMLHttpRequest` oraz `Idempotency-Key` z niepustym UUID. Podgląd jest odczytem bez body, CSRF i klucza ponowienia. Nieznane pola body są odrzucane. Daty i autor pochodzą z serwera.

| Metoda / końcówka | Wejście | Wynik |
| --- | --- | --- |
| `POST /promotions` | `productId`, `name`, `discountType`, `discountValue` | `201`, `{replayed:false,promotion}` |
| `GET /promotions` | Query: `page=1`, `pageSize=20` (1–100), opcjonalnie `status=Draft/Active/Ended`, `productId` | `{items,page,pageSize,totalItems,totalPages}` |
| `GET /promotions/{id}` | UUID | `promotion` |
| `PUT /promotions/{id}` | Pełna reguła jak przy tworzeniu oraz `expectedVersion` | Zaktualizowany szkic w `{replayed,promotion}` |
| `POST /promotions/{id}/preview` | Query `page=1`, `pageSize=20` (1–100), bez body | `{id,version,canActivate,page,pageSize,totalVariants,errors,expectedPrices,items}` |
| `POST /promotions/{id}/activate` | `expectedVersion`, `variants:[{variantId,expectedPriceChangeId}]` | Atomowy start, `{replayed,promotion}` |
| `POST /promotions/{id}/end` | `expectedVersion` | Atomowy koniec, `{replayed,promotion}` |

`promotion`: `id`, `productId`, `name`, `discountType`, `discountValue`, `currency`, `status`, `version`, daty `createdAt`, `updatedAt`, `activatedAt`, `endedAt` oraz `variants` z `variantId`, `startedAt`, `endedAt`, `priorAmount`, `referenceStart`. W szkicu `variants` jest puste. `version` rośnie po każdej edycji/start/koniec.

`preview.items` zawiera `variantId`, `sku`, `expectedPriceChangeId`, `baseAmount`, `sellingAmount`, `priorAmount`, `referenceStart`, `referenceBasis`, `errors`. Tylko `items` jest stronicowane. `expectedPrices` zawiera minimalną listę wersji **wszystkich** wariantów, a `canActivate` i `errors` dotyczą całego produktu, niezależnie od strony. Podgląd nie zapisuje ani nie rezerwuje ceny. Aktywacja ponownie sprawdza pełny skład, wersje, stan i historię.

Reguły: `percentage` w zakresie (0,100], `amount` > 0 i <= podstawy każdego wariantu, najwyżej dwa miejsca po przecinku. PLN, zaokrąglenie końcowej kwoty do grosza od połowy w górę; wynik zero dopuszczony. Jedna aktywna promocja per wariant. Produkt musi być opublikowany i mieć kategorię; wszystkie jego opublikowane warianty muszą przejść walidację. Skład zostaje zamrożony podczas startu. Cena musi być niższa od wcześniejszego minimum. Zwykła zmiana ceny w aktywnej promocji daje `409 discounted_price_conflict`.

Błędy kontrolera mają `{code,message}` (błędy automatycznej walidacji MVC mogą mieć format ProblemDetails): `400` dla danych, wersji, klucza i paginacji, `401`/`403` dla dostępu i CSRF, `404` dla promocji/produktu, `409` dla stanu, wersji, składu/ceny i klucza, `503` przy niedostępności Auth/bazy. Typowe kody konfliktów: `promotion_version_conflict`, `promotion_state_conflict`, `promotion_not_ready`, `price_or_scope_conflict`, `price_timestamp_conflict`, `idempotency_key_conflict`.

Podgląd może zwrócić `200` i `canActivate=false`: sprawdź `errors` całego produktu (`promotion_not_draft`, `product_not_visible`, `no_variants`) i wariantów (`missing_price`, `future_price`, `active_promotion`, `unmanaged_discount`, `invalid_discount`, `incomplete_or_empty_offer_history`, `not_below_prior_price`). Niekompletna historia importowanego wariantu nie jest zastępowana ceną podstawową. W takim przypadku użyj wiarygodnych danych albo poczekaj na pełne okno; nie cofaj dat rzeczywistych ofert dla obejścia walidacji.

Klucz operacji jest unikalny per autor w całym module promocji, także między trasami. Zwykła zmiana ceny ma osobną przestrzeń kluczy. Nowa komenda wymaga nowego klucza; ponowienie po utracie odpowiedzi używa tego samego klucza i identycznej znormalizowanej treści. Udane ponowienie daje `200`, `replayed=true` i oryginalną odpowiedź, również po dalszych zmianach statusu. Nie uruchamia ponownie zakończonej akcji. Nieudana komenda nie rezerwuje klucza.

### Pełny test w Postmanie

Założenie: `{{host}}` zawiera już `https://localhost:3100/api/v1/web`; po restarcie API zaloguj konto administratora jak w [administrator-access.md](administrator-access.md). Postman zachowuje cookie sesji. W środowisku developerskim po świadomym resecie seedera produkt `demo-czapka` ma syntetyczną kompletną historię do tego testu. Zwykła migracja nie dorabia kompletnej historii starym produktom.

1. `GET {{host}}/products?slug=demo-czapka`: zapisz główne `id` jako `productId`.
2. `POST {{host}}/admin/promotions`, nagłówki `Content-Type: application/json`, `X-Requested-With: XMLHttpRequest`, `Idempotency-Key: {{$guid}}`, body:

```json
{
  "productId": "{{productId}}",
  "name": "Test -20%",
  "discountType": "percentage",
  "discountValue": 20
}
```

Z odpowiedzi `201` zapisz `promotion.id` jako `promotionId`. Na tym etapie ceny się nie zmieniają. UUID dynamiczne `{{$guid}}` nadaje się do nowej operacji; do rzeczywistego ponowienia skopiuj i zachowaj wysłany UUID, ponieważ kolejne rozwinięcie zmiennej w Postmanie tworzy inny klucz.

3. `POST {{host}}/admin/promotions/{{promotionId}}/preview?page=1&pageSize=20`, bez body. Sprawdź `canActivate=true`. Możesz w zakładce Post-response zapisać:

```javascript
const preview = pm.response.json();
pm.environment.set("promotionVersion", preview.version);
pm.environment.set("promotionExpectedPrices", JSON.stringify(preview.expectedPrices));
```

4. `POST {{host}}/admin/promotions/{{promotionId}}/activate`, oba nagłówki ochronne jak przy tworzeniu, **nowy UUID** klucza, body raw JSON:

```json
{
  "expectedVersion": {{promotionVersion}},
  "variants": {{promotionExpectedPrices}}
}
```

Oczekuj `200`, `promotion.status=Active`; zapamiętaj zwrócone `promotion.version`. Ponowienie tego samego body i tego samego rzeczywistego klucza daje `replayed=true`, bez nowych cen. Nie aktualizuj wersji wewnątrz ponawianego żądania.

5. `GET {{host}}/products?slug=demo-czapka` i `GET {{host}}/products/catalog?q=Czapka` pokazują obniżkę. Sprawdź też `POST {{host}}/products/search` z body `{"q":"Czapka","minPrice":0,"maxPrice":100,"sort":"price-asc"}`. Filtry działają po cenie po rabacie.
6. `POST {{host}}/admin/promotions/{{promotionId}}/end`, **nowy UUID** klucza, nagłówek CSRF i body `{"expectedVersion":2}` (albo bieżąca wersja z aktywacji, jeżeli wcześniej edytowano szkic). Status `Ended`, kolejny wpis ceny bez rabatu, w publicznym odczycie `reduction=null`.
7. Powtórz zakończenie z zachowanym kluczem i body — brak nowych rekordów. Następnie spróbuj z nowym kluczem uruchomić zakończoną promocję: `409`. Zwykłe konto bez uprawnienia dla każdej trasy powinno dostać `403`.

### Publiczne ceny na wariancie i kafelku

`currentPrices` szczegółów i `pricesFrom` kafelka zachowują `amount`, `currency` i dostają `baseAmount`, `variantId`, `reduction`. `amount` jest ceną oferowaną. Przy aktywnej promocji:

```json
{
  "amount": 80.00,
  "currency": "PLN",
  "baseAmount": 100.00,
  "variantId": "uuid-wariantu",
  "reduction": {
    "priorAmount": 90.00,
    "referenceStart": "2026-10-01T10:00:00Z",
    "startedAt": "2026-10-31T11:00:00Z",
    "basis": "Full30Days",
    "savingPercent": 11.11
  }
}
```

Reguła może wynosić 20% od 100 PLN, ale zniżka względem wcześniejszej ceny 90 PLN to 11,11%. UI komunikujące procent korzysta z `savingPercent`. `basis=SinceFirstOffer` oznacza znany krótszy okres. Przy tej samej obniżce `priorAmount` nie przesuwa się codziennie. Wszystkie kwoty na kafelku pochodzą z jednego najtańszego pasującego wariantu per waluta; remis rozstrzyga UUID. `reduction=null` oznacza brak zweryfikowanego zdarzenia obniżki, również dla starych rabatów demo — UI nie powinno dopowiadać wtedy oznaczenia promocji.

## Kolekcje i promocje kolekcji — 01.11.2026

Kolekcja jest niezależną od kategorii, ręczną grupą produktów. Wiele kolekcji może zawierać ten sam produkt. Wdrożone reguły i scenariusze są w [collection-implementation-plan.md](collection-implementation-plan.md).

Publiczne trasy:

| Endpoint | Wejście i wynik |
| --- | --- |
| `GET /api/v1/web/collections` | `page=1`, `pageSize=20` (1–100); `{items,page,pageSize,totalItems,totalPages}` tylko dla Published |
| `GET /api/v1/web/collections/details` | Dokładnie jedno `id` albo `slug`; metadane jednej Published, w przeciwnym razie `404` |
| `GET /api/v1/web/products/by-collection` | Dokładnie jedno `collectionId` albo `slug`; `q`, `sort`, `minPrice`, `maxPrice`, opcjonalne `page`/`pageSize` i dotychczasowy `ProductListResponse` z `items`/`pagination` |
| `POST /api/v1/web/products/search` | Opcjonalne `collectionId` albo `collectionSlug`; dotychczasowe cechy, cena, ranking, stronicowanie i `availableFilters` działają w wybranej kolekcji |
| `GET /api/v1/web/products/filter-options` | Opcjonalne `collectionId` albo `collectionSlug`, niezależne od istniejącego selektora kategorii `categoryId`/`slug` |

Jednoczesna kolekcja i kategoria to przecięcie zakresów; kategoria zachowuje poddrzewo. Ukrycie kolekcji nie ukrywa produktu w katalogu i nie kończy promocji. Opublikowana kolekcja, której produkty później wycofano, może pozostać widoczna z pustą listą.

Metadane kolekcji: `id`, `displayName`, `slug`, `status`, `version`, `createdAt`, `updatedAt`, `productCount`. Publiczny `productCount` liczy tylko produkty widoczne; administracyjny wszystkie przypisania. `displayName` po trim 1–120, slug po trim/lowercase 1–160, globalnie unikalny, format jak slug produktu. Zmiana nazwy nie zmienia slugu. Pierwsza wersja nie generuje go automatycznie i nie ma fizycznego usuwania kolekcji.

Administracyjne trasy pod `/api/v1/web/admin`:

| Endpoint | Body / wynik |
| --- | --- |
| `POST /collections` | `{displayName,slug}`, tworzy Draft; `201` lub `200` ponowienie, `{replayed,collection}` |
| `GET /collections` | `page`, `pageSize`, opcjonalne `status=Draft/Published/Hidden`; lista z paginacją |
| `GET /collections/{id}` | Metadane i wszystkie przypisania liczone w `productCount` |
| `PUT /collections/{id}` | `{displayName,slug,status,expectedVersion}`; `{replayed,collection}` |
| `GET /collections/{id}/products` | `page`/`pageSize`; `{collectionVersion,productIds,items,page,pageSize,totalItems,totalPages}`; `productIds` to pełny minimalny zestaw, `items` są stronicowane i zawierają też produkty ukryte |
| `PUT /collections/{id}/products` | `{expectedVersion,productIds:[...]}` zastępuje cały skład; `{replayed,collection}` |

Każda trasa administracyjna sprawdza bieżącą sesję i `app.administrators`. Mutacje wymagają `X-Requested-With: XMLHttpRequest` i UUID `Idempotency-Key`; limit body 1 MiB, zestaw do 10 000 unikalnych, niepustych ID. `expectedVersion` pobiera się z ostatniej odpowiedzi. Wersja jest nadawana przez bazę i może wzrosnąć o więcej niż 1, gdy zmieniono wiele przypisań. Ponowienie zapisanej komendy nie zmienia wersji. Klucze kolekcji mają oddzielną przestrzeń od cen i promocji. Nieznane pola są odrzucane. Listy domyślnie 1/20, maksymalny rozmiar 100.

Publikacja i zapis opublikowanego składu wymagają publicznego produktu. Szkic/ukryta kolekcja może być pusta i zawierać produkty Draft/Hidden. Operacje nie zmieniają statusów produktów. Błędy: `400` dane/selektory/klucz, `401`/`403` sesja/uprawnienia/CSRF, `404` kolekcja lub produkt, `409` `collection_version_conflict`, `slug_conflict`, `collection_has_no_public_products`, `idempotency_key_conflict`, `503` niedostępność. Błędy automatyczne MVC mogą mieć ProblemDetails, a przekroczenie limitu body `413`.

### Rozszerzony kontrakt promocji

Te same trasy `/admin/promotions` obsługują `scope=product/collection`. Pominięty scope oznacza product, więc stare body z `productId` działa nadal. Dla kolekcji wymagane jest `collectionId`, a `productId` nie może być podane; dla produktu odwrotnie. Listę można filtrować `scope` i `collectionId`; dotychczasowy filtr `productId` oznacza bezpośredni cel promocji produktu.

Odpowiedź `promotion` dodaje `scope`, `collectionId`; dla kolekcji `productId=null`. Stare zapisane odpowiedzi są odtwarzane bez przepisywania dziennika. Podgląd dodaje `collectionVersion`, `excluded` (`productId`, opcjonalny `variantId`, `reason`); element wariantu ma również `productId`/`productName`. Ukryte produkty/warianty są diagnozowane jako wykluczone. Błąd ceny, odniesienia lub konflikt aktywnego udziału któregokolwiek objętego wariantu blokuje całą aktywację.

Dla kolekcji aktywacja wymaga `{expectedVersion,expectedCollectionVersion,variants:[...]}`. Limit objętych wariantów: 3000; podgląd większej kolekcji ma `canActivate=false`, `errors` z `variant_limit_exceeded`, bez ładowania historii cen całego zakresu. Body aktywacji ma limit 1 MiB. Zmiana kolekcji po podglądzie daje `409 collection_version_conflict`. Pełny skład/wersje cen są nadal sprawdzane. Zakończenie wymaga tylko wersji promocji, działa na utrwalonych udziałach także po zmianie składu/ukryciu kolekcji i nie ma limitu 3000 względem zakończenia już zapisanej akcji. Wyniki końca/startu są ponawialne tak jak dla produktu.

### Przykład w Postmanie

`{{host}}` oznacza bazę wraz z `/api/v1/web`. Zaloguj administratora; każda nowa mutacja ma nowy UUID `Idempotency-Key` i nagłówek CSRF. Do ponowienia zachowaj rzeczywiście wysłany UUID (dynamiczne `{{$guid}}` generuje inny przy kolejnym wysłaniu).

1. `POST {{host}}/admin/collections`:

```json
{"displayName":"Letnia kolekcja","slug":"letnia-kolekcja"}
```

Zapisz `collection.id` i `collection.version`. Pobierz ID istniejących produktów, np. `demo-czapka` i `demo-produkt-11` z developerskiego seedera.

2. `PUT {{host}}/admin/collections/{{collectionId}}/products`:

```json
{"expectedVersion":{{collectionVersion}},"productIds":["{{productIdA}}","{{productIdB}}"]}
```

3. Zaktualizuj `collectionVersion` z odpowiedzi i opublikuj przez `PUT {{host}}/admin/collections/{{collectionId}}`:

```json
{"displayName":"Letnia kolekcja","slug":"letnia-kolekcja","status":"Published","expectedVersion":{{collectionVersion}}}
```

4. `POST {{host}}/admin/promotions`:

```json
{"scope":"collection","collectionId":"{{collectionId}}","name":"Letnia kolekcja -20%","discountType":"percentage","discountValue":20}
```

5. `POST {{host}}/admin/promotions/{{promotionId}}/preview?pageSize=20`. Przy `canActivate=true` zapisz pola:

```javascript
const p = pm.response.json();
pm.environment.set("promotionVersion", p.version);
pm.environment.set("collectionVersion", p.collectionVersion);
pm.environment.set("expectedPrices", JSON.stringify(p.expectedPrices));
```

6. `POST {{host}}/admin/promotions/{{promotionId}}/activate`:

```json
{"expectedVersion":{{promotionVersion}},"expectedCollectionVersion":{{collectionVersion}},"variants":{{expectedPrices}}}
```

7. Sprawdź `GET {{host}}/collections/details?slug=letnia-kolekcja`, `GET {{host}}/products/by-collection?slug=letnia-kolekcja&page=1&pageSize=20` i POST wyszukiwania:

```json
{"collectionSlug":"letnia-kolekcja","minPrice":0,"maxPrice":100,"sort":"price-asc","page":1,"pageSize":20}
```

Cechy nadal dodaje się dynamicznymi parami `attributes:[{code,values}]`. Można równocześnie dodać selektor kategorii.

8. `POST {{host}}/admin/promotions/{{promotionId}}/end`, body `{"expectedVersion":{{activePromotionVersion}}}`. Wersja pochodzi z odpowiedzi aktywacji, a nie kolekcji. Produkt usunięty w międzyczasie z kolekcji też odzyska cenę podstawową. Szczegóły publicznego `reduction` pozostają takie jak dla promocji produktu.
