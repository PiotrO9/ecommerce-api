# Zarządzanie ofertą — analiza i plan wdrożenia

Cel: zaplanować administracyjne przygotowywanie, edycję i publikowanie produktów, wariantów, kategorii i cech, z podstawową obsługą magazynu. Dokument rozwija moduły 2.2–2.7 oraz część magazynową planu backendu. Powstał na prośbę właściciela projektu 01.11.2026.

**Status: O0–O9 wdrożone w kodzie; O10 — odbiór lokalny wykonany, wdrożenie środowiskowe pozostaje otwarte.** Użytkownik polecił realizację planu 01.11.2026; przyjęto rekomendacje części 12. Części 1–12 zachowują analizę i projekt z chwili planowania; aktualne działanie, użycie i granice odbioru opisuje część 13. Nowych migracji nie zastosowano w Supabase. Koszyk, ulubione i powiązanie z użytkownikiem rozwija równolegle drugi agent.

## 1. Stan przed wdrożeniem i rzeczywista luka

| Obszar | Potwierdzone w kodzie | Co trzeba dobudować |
| --- | --- | --- |
| Produkt i wariant | Encje, UUID, statusy `Draft`/`Published`/`Hidden`, daty, SKU, opisy i uporządkowane URL-e zdjęć wariantu. | Tworzenie i edycję, kontrolę współbieżnych zmian, przejścia statusów i znaczenie daty publikacji. |
| Relacje wariantów | Odroczone ograniczenia PostgreSQL wymagają minimum jednego wariantu produktu, także szkicu, i różnych zestawów par cech w obrębie produktu. Dwa puste zestawy też są identyczne. | Operacje zapisujące poprawny graf w jednej transakcji oraz testy konkurujących zapisów tych ograniczeń. |
| Kategorie | Drzewo, brak cykli, unikalny slug wśród rodzeństwa, blokady FK, publiczny odczyt drzewa, szczegółów i produktów poddrzewa. | Tworzenie, zmianę nazwy/slugu, przenoszenie, usuwanie pustego liścia i przypisania produktów. |
| Cechy | Wspólne `AttributeKey` i `AttributePair`, relacja wiele-do-wielu z wariantem, wiele wartości tego samego klucza dozwolone. | Administracyjny słownik, jednoznaczną normalizację wartości i bezpieczną zmianę zestawu cech. |
| Unikalność tekstów | Kod klucza jest przycinany i zmieniany na małe litery. SKU jest unikalne według zapisanej postaci. Para jest unikalna według `(AttributeKeyId, Value)`, bez pełnej normalizacji wartości. | Rozstrzygnięcie semantyki wielkości liter i białych znaków, audyt już zapisanych danych. |
| Publikacja | Publiczny odczyt filtruje statusy i wymaga obowiązującej ceny wariantu. Model koncepcyjny wymaga kategorii oraz minimum jednego opublikowanego wariantu. | Egzekwowanie warunków podczas zapisu; sam odczyt nie stanowi kompletnej walidacji publikacji. |
| Ceny i promocje | Są administracyjne zmiany ceny PLN, historia, kontrola wersji ceny, ponowienia, promocje produktu i kolekcji oraz utrwalone odniesienie wcześniejszej ceny. | Wykorzystanie tych operacji w procesie przygotowania oferty, bez drugiego kalkulatora cen. |
| Kolekcje | Administracja i publiczne odczyty są wdrożone; wersje kolekcji i dziennik operacji obsługują konflikty i ponowienia. | Zgodność nowych zmian produktów z istniejącymi regułami kolekcji. |
| Magazyn | Jeden opcjonalny `VariantStock` na wariant, ilość nieujemna. Brak rekordu jest stanem nieznanym, różnym od zera. | Odczyt administracyjny, inicjalizacja i ręczna korekta z kontrolą wersji. Rezerwacje pozostają osobnym modułem. |
| Uprawnienia | Istnieją `AdministratorAccess`, zweryfikowana sesja i ochrona zapisów nagłówkiem `X-Requested-With`. | Użycie tego samego wzorca w nowych endpointach; bez nowego systemu ról. |
| Seeder | Tworzy cały przykładowy katalog i czyści jawnie 17 tabel w jednej transakcji, bez `CASCADE`. | Uwzględnienie wersji, nowych tabel, kolejności publikacji oraz FK koszyka i ulubionych. |

Sprawdzone źródła: `Entities/`, `Data/CatalogModelConfiguration.cs`, `Data/CollectionModelConfiguration.cs`, `Data/Migrations/`, `Controllers/`, `Services/Catalog/CollectionService.cs`, `Services/Pricing/`, `Services/Development/DemoCatalogSeeder.cs` i dokumenty wymienione niżej. To analiza drzewa roboczego, nie nowy odbiór wdrożenia w Supabase.

Istotna granica dokumentacji: [model katalogu](catalog-model.md) opisuje warunki publikacji jako ustalenia koncepcyjne i wskazuje brak operacji zapisu. Obecne ograniczenia bazy nie realizują jeszcze całej tej reguły. Nie należy odznaczać punktów 2.2–2.7 tylko dlatego, że tabele i publiczne odczyty już istnieją.

## 2. Zakres pierwszej wersji

Rezultatem ma być możliwość przejścia przez proces: **kategoria i cechy → produkt ze swoim pierwszym wariantem jako szkic → cena i stan → kontrola gotowości → publikacja → późniejsza edycja lub ukrycie**. Przygotowanie oferty ma być możliwe przez API/Postmana, niezależnie od przyszłego panelu CRM.

W zakresie:

- Administracyjne odczyty szkiców, ukrytej i opublikowanej oferty, oddzielone od publicznych DTO.
- Tworzenie produktu razem z pierwszym wariantem, następnie kolejnych wariantów.
- Edycja metadanych, przypisań kategorii, konfiguracji wariantu w ustalonych granicach, opisów i URL-i zdjęć.
- Słownik kluczy cech i współdzielonych wartości; żadnych stałych pól `color`, `size` itp.
- Operacje drzewa kategorii i publikacji oraz inicjalizacja/korekta stanu wariantu.
- Wersjonowanie edycji, ponowienia zapisów, audyt autora i testy PostgreSQL.

Poza zakresem: logowanie i dane właściciela koszyka, scalanie koszyków, rezerwacje, checkout, zamówienia, masowy import, edycja hurtowa, harmonogram publikacji, promocja całego katalogu, upload do Supabase Storage i budowa panelu frontendowego. Ceny, promocje i kolekcje wykorzystują istniejące endpointy.

**Rekomendacja: bez fizycznego usuwania produktów i wariantów w pierwszej wersji.** Ukrycie wystarcza do wycofania oferty i zachowuje powiązania historii cen, promocji, koszyka i ulubionych. Usuwanie kategorii pozostaje możliwe wyłącznie dla liścia bez przypisanych produktów, zgodnie z dotychczasową decyzją. Nie dodajemy kaskadowego usuwania danych zakupowych.

## 3. Proponowane reguły produktu i wariantu

### Tworzenie i edycja

1. `POST /products` przyjmuje produkt i wymagany `firstVariant`. Oba powstają jako `Draft` w jednej transakcji. Szkic może nie mieć kategorii, ceny, stanu ani zdjęć. SKU i poprawny slug nadal są wymagane.
2. Nowy wariant powstaje jako `Draft`, od razu z docelowym zestawem par cech. Tworzenie drugiego pustego wariantu przy istniejącym pustym zestawie kończy się konfliktem; tymczasowy szkic nie omija unikalności konfiguracji.
3. UUID produktu i wariantu pozostają stałe. Nie przenosimy istniejącego wariantu do innego produktu przez edycję `ProductId`.
4. SKU można poprawić, zachowując UUID i unikalność. Proponujemy przycinać skrajne białe znaki, zachowując obecną w bazie wrażliwość na wielkość liter. Zmiana tej semantyki wymaga wcześniejszego audytu kolizji.
5. Cena nie jest polem nowych DTO metadanych. Po utworzeniu wariantu administrator używa istniejącego endpointu zmiany ceny. Nie wywołujemy serwisu otwierającego własną transakcję wewnątrz transakcji tworzenia produktu.
6. Zmiana konfiguracji wcześniej sprzedawanego/oferowanego wariantu może zmienić znaczenie pozycji koszyka i historii. Proponujemy pozwalać na nią tylko dla szkicu bez historii ceny, publikacji i udziału w promocji. Dla innej konfiguracji powstaje nowy wariant. To nowa reguła do zatwierdzenia, a nie obecne ograniczenie bazy. Korekta opisu, zdjęć lub SKU nie zmienia konfiguracji.

### Publikacja i ukrywanie

| Operacja | Proponowany warunek i skutek |
| --- | --- |
| Publikacja wariantu | Ma obowiązującą teraz cenę PLN, poprawne wymagane pola i konfigurację. Może być przygotowany jako `Published` pod produktem `Draft`; produkt nadal nie jest publicznie widoczny. |
| Publikacja produktu | Ma co najmniej jedną kategorię i jeden wariant `Published`. Opublikowane warianty spełniają warunki publikacji. |
| Ukrycie produktu | Ukrywa całą ofertę bez przepisywania statusów wariantów, usuwania cen, kończenia promocji ani usuwania pozycji klienta. |
| Ukrycie ostatniego opublikowanego wariantu | Gdy produkt nadal jest `Published`, zwracamy konflikt. Administrator najpierw ukrywa produkt. |
| Usunięcie ostatniej kategorii | Dla produktu `Published` konflikt; dla szkicu/ukrytego produktu dozwolone. |
| Ponowna publikacja | Zawsze ponowna walidacja aktualnego stanu, bez odtwarzania dawnych założeń o cenie i dostępności. |

Proponujemy jawne przejścia między wszystkimi trzema statusami, przy spełnieniu warunków docelowego stanu. Przejście przez `Hidden` lub `Draft` nie usuwa historii i nie umożliwia obejścia ochrony tożsamości wariantu.

`PublishedAt` proponujemy traktować jako datę pierwszej zarejestrowanej publikacji danego obiektu, bez nadpisywania przy ponownym pokazaniu. Dla wariantu nie oznacza ona automatycznie początku faktycznego oferowania: nadrzędny produkt może być jeszcze szkicem. Okresy faktycznej oferty nadal wynikają z istniejącej historii oferty. Nie wymyślamy dawnych dat publikacji dla importowanych danych z `null`.

Brak magazynu i stan `0` powinny być ostrzeżeniem gotowości, bez blokowania samej publikacji w tej wersji. Nie zmienia to zasady blokowania zakupu przy nieznanym stanie ani reguł koszyka ustalanych w jego module. Wymagalność zdjęcia/opisu przed publikacją pozostaje propozycją, nie potwierdzonym wymaganiem.

Odczyt gotowości zwraca `canPublish`, stabilne kody blokad, ścieżki pól i ostrzeżenia, np. `missing_category`, `no_published_variant`, `missing_current_pln_price`, `unknown_stock`. Szczegóły wariantów są stronicowane, wynik ogólny dotyczy całego produktu. Zapis ponownie sprawdza warunki w transakcji; podgląd nie rezerwuje ceny ani stanu.

## 4. Kategorie, cechy i zdjęcia

### Kategorie i slugi

- Zachowujemy dotychczasowe limity, wzorzec slugu i unikalność: produkt globalnie, kategoria wśród rodzeństwa. UUID jest jednoznacznym identyfikatorem administracyjnym, także przy powtarzających się slugach w różnych gałęziach.
- Przy tworzeniu można podać slug albo wygenerować go z nazwy. Generator ma testy polskich znaków, separatorów i limitu długości; gdy nie potrafi utworzyć sensownego slugu, prosi o ręczny. Kolizja daje `409`, bez niejawnego zmieniania adresu losowym sufiksem.
- Zmiana nazwy nie zmienia automatycznie slugu. Edycja slugu i przeniesienie kategorii walidują rodzeństwo, brak cyklu i istnienie rodzica.
- Nie usuwamy kategorii z podkategoriami lub jakimikolwiek przypisanymi produktami, także ukrytymi. Najpierw trzeba świadomie przenieść przypisania.
- Drzewo administracyjne nie dostaje paginacji. Zachowujemy stabilną kolejność; ręczne pozycjonowanie i przekierowania dawnych URL-i są odłożone do wymagań widoku.

### Współdzielony słownik cech

- Klucz ma techniczne `Code` oraz `DisplayName` **klucza**. Nie dodajemy tu `DisplayName` wartości. Proponujemy niezmienne `Code` po utworzeniu i wersjonowaną edycję samej etykiety.
- Utworzenie istniejącego klucza/pary po normalizacji zwraca istniejący zasób, zamiast tworzyć duplikat. Konfliktująca etykieta istniejącego kodu wymaga jawnej edycji, nie jej cichego nadpisania.
- Proponowana normalizacja wartości: Unicode NFC i przycięcie skrajnych białych znaków; zachowanie wielkości liter oraz spacji wewnętrznych. `S` i `s` nie są automatycznie tym samym w uniwersalnym sklepie. Dla kodu klucza nadal obowiązuje przycięcie i małe litery.
- Implementacja musi ustalić wspólny zestaw znaków przycinanych w .NET i SQL. PostgreSQL `btrim()` domyślnie usuwa spację, więc nie jest odpowiednikiem pełnego `.Trim()`; samo dodanie takiego CHECK nie rozwiązuje normalizacji. [Dokumentacja funkcji tekstowych PostgreSQL](https://www.postgresql.org/docs/17/functions-string.html).
- Przed migracją sprawdzamy kolizje istniejących wartości. Nie scalamy automatycznie rekordów, bo zmiana ID par może stworzyć dwa identyczne zestawy wariantów lub zmienić ich znaczenie.
- Para pozostaje współdzielonym, niezmiennym znaczeniem `(klucz, wartość)`. Pierwsza wersja nie oferuje globalnego przemianowania ani usuwania używanej pary. Powstaje nowa para, a jej przypisanie jest zmieniane tylko dla wariantów, dla których taka edycja jest dozwolona.
- Zestaw par wariantu jest zastępowany atomowo; zmienione linki zapisujemy jako usunięcie i dodanie, zgodnie z obecnym ograniczeniem bazy. Odrzucamy duplikaty ID w body. Wiele różnych wartości jednego klucza pozostaje dozwolone.

### Opisy i URL-e

Zachowujemy zdjęcia oraz oba opisy na wariancie. Proponowane limity wejścia dodatkowo do obecnych limitów bazy: maksymalnie 20 URL-i po 2048 znaków, opis do 20 000 znaków i do 100 par na wariant. Limity te są nową propozycją do potwierdzenia. Opis w pierwszej wersji jest zwykłym tekstem; rich text wymaga osobnego kontraktu i sanitacji.

URL musi być bezwzględnym adresem HTTPS bez danych logowania. Zachowujemy kolejność zdjęć. Backend nie pobiera adresów podczas walidacji, a upload, fizyczne kasowanie plików i zarządzanie Storage są osobnym zadaniem. Publiczne DTO pozostają zgodne z obecnym kontraktem.

## 5. Baza, wersje i współbieżność

### Zmiany schematu

| Obiekt | Planowana zmiana |
| --- | --- |
| `products` | `version bigint`, mapowanie concurrency token, wersjonowanie zmian metadanych i grafu produktu. |
| `product_variants` | `version bigint`, wersjonowanie metadanych, statusu i zestawu cech. |
| `categories` | `version bigint` dla nazwy, slugu i rodzica. |
| `attribute_keys` | `version bigint` dla edycji `DisplayName`. |
| `attribute_pairs` | Uzgodniona kanoniczna postać wartości i odpowiadające jej zabezpieczenie; bez wersji dla niezmiennej pary. |
| `variant_stock` | `version bigint` dla inicjalizacji i korekt ilości. |
| `catalog_operations` | Nowy dziennik skutecznych operacji i ponowień. |
| Funkcje i wyzwalacze | Wersje, propagacja wersji grafu, uzupełnione blokady oraz odroczone warunki publikacji. |

Nowa tabela pozostaje w prywatnym schemacie `app`, z RLS i uprawnieniami zgodnymi z istniejącym wzorcem tabel aplikacji; nie udostępniamy dziennika przez Supabase Data API. Autor pochodzi ze zweryfikowanej sesji. Jego zapis audytowy nie powinien znikać wraz z późniejszym odebraniem uprawnienia administratora.

Daty `UpdatedAt` zostają datami. Nie zastępują wersji: nie są obecnie concurrency tokenem, a kilka zmian w jednej transakcji może mieć ten sam czas. EF Core musi porównywać wersję odczytaną przez klienta z aktualną podczas zapisu. [Dokumentacja współbieżności EF Core](https://learn.microsoft.com/en-us/ef/core/saving/concurrency).

Proponowana własność wersji:

- Produkt: metadane i status produktu, przypisania kategorii oraz dodanie/edycja wariantu podnoszą wersję produktu.
- Wariant: jego metadane/status i linki cech podnoszą wersję wariantu; propagują zmianę do produktu.
- Cena i magazyn mają własną kontrolę zmian, bez automatycznego podnoszenia wersji opisowej produktu. Publikacja sprawdza ich aktualny stan niezależnie.
- Zmiana etykiety globalnego klucza wersjonuje klucz, bez aktualizacji wszystkich produktów. Kategoria wersjonuje własne dane, bez udawania wersji całego poddrzewa.

Wersje generuje baza, podobnie jak dla kolekcji; nie podbijamy ich równolegle w kodzie. Skoki o więcej niż jeden są poprawne. Po zapisach relacji trzeba ponownie odczytać końcowe wersje, aby odpowiedź nie zawierała wartości sprzed wyzwalaczy. Odczyt szczegółów administracyjnych ma spójny obraz danych, np. krótką transakcję `RepeatableRead`, bez blokowania wszystkich piszących.

### Transakcja i kolejność blokad

Zachowujemy istniejący `CatalogWriteLock` z blokadą `(20261031, 3)` jako wspólny porządek zapisów oferty, cen i promocji. Obecnie nie obejmuje wszystkich nowych tabel: trzeba rozszerzyć zabezpieczenie m.in. kategorii, słownika, linków cech i magazynu. Nie tworzymy niezależnego porządku blokad dla każdego nowego kontrolera.

Kolejność operacji: weryfikacja sesji/uprawnień poza transakcją → transakcja → globalna blokada katalogu → sprawdzenie ponowienia → blokady zasobów i dotychczasowa blokada drzewa kategorii → wersja i reguły → zapis → końcowe wersje i dziennik → commit. Dokładną kolejność blokad trzeba sprawdzić także dla surowego SQL i wyzwalaczy. W transakcji nie wykonujemy połączeń do Supabase Auth, Storage ani poczty.

Tworzenie grafu oraz publikacja wymagają atomowości także przy kilku `SaveChanges`. Odroczone ograniczenie może zgłosić błąd dopiero przy `CommitAsync`; obsługa wyjątków musi obejmować ten moment i wycofać również dziennik operacji. [Transakcje EF Core](https://learn.microsoft.com/en-us/ef/core/saving/transactions), [odroczone wyzwalacze PostgreSQL](https://www.postgresql.org/docs/17/sql-createtrigger.html).

Nowe ograniczenia publikacji powinny sprawdzać stan końcowy grafu produktu po zmianie jego statusu, wariantów lub przypisań kategorii. Nie dodajemy drogi kasowania/aktualizacji historii cen. Część obecnych testów odczytu celowo tworzy warianty bez bieżącej ceny PLN; przy nowej regule należy odróżnić wspierany odczyt danych historycznych od nowej operacji publikacji, zamiast bezrefleksyjnie zmieniać testowe kwoty lub daty.

### Audyt i ponowienia

Proponowane `CatalogOperation`: UUID, rodzaj operacji, typ i ID zasobu, zweryfikowany autor UUID, klucz ponowienia UUID, wersjonowany skrót żądania, oczekiwana wersja, data UTC oraz minimalny wynik potrzebny do odtworzenia odpowiedzi. Korekta magazynu zapisuje także powód i ilość przed/po. Nie przechowujemy kopii całych opisów i zdjęć bez potrzeby.

- Wymagamy `Idempotency-Key` dla nowych zapisów, jak w istniejących operacjach administracyjnych. Unikalność `(autor, klucz)` obejmuje przestrzeń operacji katalogu; ceny/promocje/kolekcje zachowują dotychczasowe dzienniki.
- To samo żądanie i klucz odtwarzają poprzedni sukces, także gdy zasób później się zmienił lub kategorię usunięto. Inna treść lub cel przy tym samym kluczu daje konflikt. Bieżące uprawnienia sprawdzamy również przy ponowieniu.
- Sprawdzenie dziennika poprzedza sprawdzenie obecnego istnienia zasobu i jego wersji. Ponowienie nie zwiększa wersji ani nie dopisuje kolejnego wpisu.
- Skrót uwzględnia rodzaj operacji, zasób, oczekiwaną wersję i znormalizowane body. Zbiory ID sortujemy, kolejność zdjęć zachowujemy; format skrótu ma wersję, żeby przyszłe DTO nie zepsuły dawnych ponowień.
- Typ i ID zasobu są celowo polimorficzne, bez FK blokującego dozwolone usunięcie kategorii; dozwolone typy ograniczamy w bazie. Dziennik nie może utrudniać odtworzenia sukcesu po usunięciu zasobu.
- Sukces i jego dziennik zapisujemy razem. Nieudane walidacje nie rezerwują skutecznego wyniku. Retencję dziennika trzeba ustalić przed ewentualnym czyszczeniem; zadanie 30-dniowe nie usuwa go automatycznie.

## 6. Proponowane API

Wspólny prefiks: **`/api/v1/web/admin`**. To nowe trasy planowane, nie lista już dostępnych endpointów. Wszystkie wymagają administratora; zapisy również ochrony `X-Requested-With` i klucza ponowienia. Istniejące publiczne endpointy pozostają bez zmian.

| Metoda i końcówka | Zadanie |
| --- | --- |
| `POST /products/search` | Stronicowana lista administracyjna, także `Draft` i `Hidden`; wyszukiwanie po nazwie/SKU, statusie, kategorii, kolekcji i brakach przygotowania. Bez pełnej historii cen i bez wszystkich wariantów w każdym wierszu. |
| `GET /products/{id}` | Metadane produktu, wersja, przypisania i podsumowanie wariantów. |
| `GET /products/{id}/variants` | Stronicowane warianty wraz z wersjami i podsumowaniem ceny/stanu. |
| `POST /products` | Atomowo produkt i pierwszy wariant jako szkice. |
| `PUT /products/{id}` | Nazwa i slug; wymagane `expectedVersion`. |
| `PUT /products/{id}/categories` | Pełny docelowy zestaw `categoryIds` i wersja produktu. |
| `GET /products/{id}/publication-readiness` | Gotowość, blokady i ostrzeżenia; bez zmiany danych. |
| `PUT /products/{id}/status` | Docelowy status, wersja produktu i ponowna walidacja. |
| `POST /products/{id}/variants` | Kolejny szkic wariantu z konfiguracją; `expectedProductVersion`. |
| `GET /variants/{id}` | Administracyjne szczegóły wariantu. |
| `PUT /variants/{id}` | SKU, opisy, uporządkowane `imageUrls` i wersja wariantu. |
| `PUT /variants/{id}/attributes` | Docelowe `attributePairIds` i wersja wariantu. |
| `GET /variants/{id}/publication-readiness` | Gotowość pojedynczego wariantu. |
| `PUT /variants/{id}/status` | Status wariantu z kontrolą wersji i reguł produktu. |
| `GET /categories`, `GET /categories/{id}` | Drzewo i szczegóły administracyjne z wersjami; drzewo bez paginacji. |
| `POST /categories` | Nazwa, opcjonalny slug i `parentId`. |
| `PUT /categories/{id}` | Nazwa, slug i `expectedVersion`. |
| `PUT /categories/{id}/parent` | Nowy `parentId` lub `null`, wersja, sprawdzenie cyklu i slugu. |
| `DELETE /categories/{id}?expectedVersion=…` | Usunięcie wyłącznie pustego liścia. Wersja w query, bez body DELETE. |
| `GET /attribute-keys`, `POST /attribute-keys` | Stronicowany odczyt i utworzenie/odnalezienie klucza po kodzie. |
| `PUT /attribute-keys/{id}` | Zmiana `displayName` klucza z wersją. |
| `GET /attribute-keys/{id}/values`, `POST /attribute-keys/{id}/values` | Stronicowany słownik par oraz utworzenie/odnalezienie znormalizowanej wartości. |
| `GET /variants/{id}/stock` | Stan z własnym ID i wersją albo jawny brak znanego stanu. |
| `PUT /variants/{id}/stock` | Pierwszy stan lub ustawienie nowej ilości, `expectedVersion` i powód. |

Administrowanie ceną nadal używa istniejących `GET /variants/{id}/price` i `POST /variants/{id}/price-changes` z `expectedPriceChangeId`. Promocje i kolekcje korzystają ze swoich wdrożonych tras. Nowy parametr `expectedVersion` nie zastępuje wersji ceny i nie zmienia uzgodnionego kontraktu cenowego.

`POST /products/search` jest odczytem: nie wymaga klucza ponowienia i nie zapisuje dziennika operacji. Administracyjne odpowiedzi stosują `Cache-Control: no-store`, podobnie jak obecna administracja kolekcji. Stronicowane listy zwracają istniejący w projekcie zestaw metadanych paginacji i mają limit wielkości strony.

Wynik mutacji zwraca ID i końcowe wersje dotkniętych zasobów; np. edycja wariantu zwraca `version` wariantu i `productVersion`. Utworzenie zwraca `201`, a odtworzenie utworzenia `200`; aktualizacje `200`, usunięcie kategorii oraz jego ponowienie `204`. Odnalezienie już istniejącego elementu słownika zwraca `200`, nowy element `201`.

Pierwszy zapis stanu wymaga jawnego `expectedVersion: null`, co oznacza „rekord jeszcze nie istnieje”; kolejne wymagają liczby. Równoległa inicjalizacja daje konflikt. Nie dodajemy kasowania stanu ani pomniejszania ilości przez koszyk. W pierwszej wersji to korekta stanu fizycznego, nie obietnica dostępności po przyszłych rezerwacjach.

Proponowane odpowiedzi błędów: `400` dla niepoprawnych pól/formatów, `401` bez sesji, `403` bez dostępu administratora, `404` dla brakującego zasobu, `409` dla nieaktualnej wersji, zajętego SKU/slugu, identycznej konfiguracji lub naruszenia reguł stanu. Błąd ma stabilne `code`, `message` i opcjonalne błędy pól. Nie ujawniamy treści SQL. Mapowanie opiera się na nazwie ograniczenia/SQLSTATE, a nie dopasowaniu lokalizowanego komunikatu; obecne wyzwalacze mogą wymagać uzupełnienia stabilnych identyfikatorów błędów.

### Przykład przyszłego procesu

Poniższe body ilustruje proponowany kontrakt `POST /api/v1/web/admin/products`, nie działa jeszcze w obecnej aplikacji. ID kategorii i par są wcześniej utworzonymi zasobami; w prostym szkicu obie listy mogą być puste.

```json
{
  "name": "Koszulka Basic",
  "slug": "koszulka-basic",
  "categoryIds": [],
  "firstVariant": {
    "sku": "BASIC-RED-S",
    "description": "Bawełniana koszulka.",
    "shortDescription": "Klasyczny krój.",
    "imageUrls": [],
    "attributePairIds": []
  }
}
```

Odpowiedź zawiera `productId`, `variantId` i ich końcowe wersje. Następnie administrator przypisuje kategorię, przez istniejący endpoint dopisuje cenę PLN i przez nowy endpoint ustawia magazyn. Publikuje wariant, odczytuje aktualną wersję produktu podniesioną przez tę zmianę, sprawdza gotowość i publikuje produkt. Dopiero wtedy sprawdza produkt w publicznym katalogu. Każda nowa operacja ma nowy klucz ponowienia; ponowne wysłanie tej samej operacji zachowuje poprzedni klucz i body.

## 7. Struktura kodu

Rekomendujemy rozszerzyć obecną architekturę, bez wprowadzania nowego frameworka CQRS ani drugiego dostępu do bazy:

- Nowe kontrolery `AdminProductsController`, `AdminVariantsController`, `AdminCategoriesController`, `AdminAttributesController`, `AdminVariantStockController`; cienka obsługa HTTP, uprawnień i kontraktów.
- DTO w oddzielnym obszarze administracyjnego katalogu; nie przyjmujemy encji EF jako requestu i nie pozwalamy ustawiać autorów, dat, wersji ani pól relacji przez dowolne body.
- Serwisy w `Services/Catalog/` dla produktów/wariantów, drzewa, słownika i gotowości oraz serwis magazynu. Podział klas dopasować do rzeczywistej wielkości, bez uniwersalnego repozytorium CRUD.
- Wspólne niewielkie komponenty dla slugu, kanonizacji cech i dziennika operacji. Wykorzystanie istniejących `AdministratorAccess` i `CatalogWriteLock`.
- `Entities/`, konfiguracje `Data/`, `AppDbContext` i migracje: wersje, dziennik i ograniczenia. Obecne serwisy cenowe nadal są jedyną drogą wyliczania/zapisywania cen administracyjnych.
- Odczyty list wykonują projekcje i pobierają ceny/stany zbiorczo. Nie ładujemy pełnego grafu z historią dla każdego produktu ani nie tworzymy zapytania na każdy wariant.

## 8. Współpraca z agentem koszyka i ulubionych

Aktualny [plan koszyka i ulubionych](cart-favorites-implementation-plan.md) wskazuje wariant UUID zarówno dla pozycji koszyka, jak i ulubionych; koszyki gościa i konta są oddzielne, bez scalania. Zarządzanie ofertą nie zmienia tych decyzji, sesji ani identyfikacji właściciela.

Kontrakt na styku modułów:

1. Identyfikatory wariantów pozostają stabilne. Ukryty towar pozostaje zapisany w danych klienta i jest prezentowany jako niedostępny według reguł modułu zakupowego; nie kasujemy automatycznie jego pozycji.
2. Koszyk odczytuje aktualny status nadrzędnego produktu, wariantu, cenę i stan. Zapis oferty nie przelicza i nie aktualizuje wszystkich koszyków.
3. Dopuszczalność dodania, zachowanie ulubionych i odczyt niedostępnej pozycji należą do agenta koszyka. Z naszej strony dostarczamy spójny stan katalogu i nie zmieniamy cen poza istniejącym mechanizmem.
4. Koszyk nie rezerwuje ani nie zmniejsza stanu. Gdy pojawi się checkout, trzeba osobno uzgodnić blokady i wersjonowanie korekt względem rezerwacji. Zapisy oferty nie blokują wierszy właścicieli koszyka; odczyt koszyka nie przejmuje globalnej blokady piszących katalogu.
5. Wspólny odczyt ceny/dostępności można wydzielić dopiero po uzgodnieniu interfejsu, bez równoległego przepisywania tych samych plików. `PublicPriceBuilder` formatuje cenę, nie jest gotowym repozytorium zapytań o dostępność.
6. `AppDbContext`, snapshot EF, rejestracja DI, seeder i wspólne dokumenty są miejscami kolizji. W danym momencie jedna osoba generuje migrację/snapshot; przed generowaniem sprawdza, czy nie wciąga nieukończonych modeli drugiej funkcji. Przy wspólnym drzewie roboczym zapisujemy i commitujemy wyłącznie własny zakres, bez `git add .`.

Najważniejszy wspólny punkt to reset seedera: FK koszyka/ulubionych do wariantu blokuje `TRUNCATE ... RESTRICT` nawet przy pustej tabeli pozycji. Trzeba jawnie ustalić developerskie usuwanie zależnych danych zakupowych i zmianę ich wersji albo odmowę resetu. Nie stosujemy `CASCADE` i nie rozszerzamy czyszczenia na konta. To zależność wymagająca wspólnego uzgodnienia przed migracją obu modułów, a nie szczegół do pominięcia na końcu.

## 9. Migracje, dane istniejące i seeder

Przed zmianą schematu wykonać raport danych: opublikowane produkty bez kategorii/widocznego wariantu, warianty bez bieżącej ceny PLN, nieznane `PublishedAt`, kolizje normalizacji cech/SKU i nowe zależności FK. Raport nie naprawia danych automatycznie. Niezgodne dane wymagają jawnego sposobu korekty; nie ukrywamy produktów ani nie dopisujemy fikcyjnych cen/dat, żeby migracja przeszła.

Migracje dzielimy: najpierw wersje i dziennik, następnie uzgodniona normalizacja i warunki publikacji po przygotowaniu poprawnych ścieżek zapisu. Ograniczenia mogą wymagać skoordynowanego wdrożenia API i migracji. Nie ruszamy `auth.users`, historycznych kwot, utrwalonych odniesień obniżek ani zapisanych skrótów starych operacji.

Po każdej zmianie modelu aktualizujemy seeder zgodnie z [seeder-maintenance.md](seeder-maintenance.md), nie dopiero po zakończeniu całego projektu. Docelowa kolejność w jednej transakcji:

1. Jawne czyszczenie uzgodnionych zależności w poprawnej kolejności, bez `CASCADE`, przy zachowaniu developerskich zabezpieczeń endpointu.
2. Kategorie, słownik, produkty i warianty `Draft`, przypisania, stany i ceny oraz szkice kolekcji — zapis grafu.
3. Publikacja gotowych wariantów, potem produktów z kategoriami, na końcu kolekcji. Nowe reguły nie mogą zależeć od przypadkowej kolejności UPDATE wygenerowanej przez EF.
4. Odczyt odświeżonych wersji po zapisach zależnych; wyzwalacze podnoszą wersje także encji śledzonych wcześniej przez EF. Bez tego kolejne zapisy mogą same sobie powodować konflikt.
5. Utrzymanie scenariuszy demonstracyjnej historii oferty/cen, liczników `created`, sumy `added`, list resetu i `ANALYZE`. Nowy dziennik katalogu obejmujemy resetem; seeder nie produkuje fikcyjnych wpisów administratora, więc jego licznik wynosi zero.

Nowa tabela oznacza rozszerzenie obecnej listy 17 tabel; ostatecznej liczby nie ustalamy przed uwzględnieniem równoległych zmian koszyka. Wersje nie są osobnym typem utworzonych rekordów. Test resetu powinien uwzględnić wcześniejsze prawdziwe zapisy administracyjne i zależności zakupowe, a nie tylko pustą bazę.

## 10. Etapy wdrożenia i kryteria odbioru

Tabela zachowuje planowany zakres i kryteria. Aktualny postęp wskazuje lista poniżej; wdrożenie środowiskowe nie jest utożsamiane z gotowością kodu.

| Etap | Prace | Warunek odbioru |
| --- | --- | --- |
| O0 | Uzgodnienie reguł z części 12, kontraktu z koszykiem i właściciela migracji; ponowna kontrola obszarów chronionych. | Zapisany zakres i kolejność pracy bez sprzecznych decyzji obu modułów. |
| O1 | Testy istniejących ograniczeń grafu na PostgreSQL; audyt danych do migracji. | Potwierdzone minimum jednego wariantu, unikalność zestawów, brak cykli i konflikty równoległych zapisów; znane niezgodności danych. |
| O2 | Wersje, dziennik, transakcje i mapowanie błędów; rozszerzenie blokad; migracja i seeder. | Dwa równoległe zapisy tej samej wersji nie nadpisują się; ponowienie nie duplikuje zmiany. |
| O3 | Administracja kategorii i generator slugów. | Można utworzyć/przenieść kategorię; cykl, konflikt slugu i usunięcie zajętej kategorii są odrzucane. |
| O4 | Administracja słownika kluczy i wartości, normalizacja. | Współdzielenie bez duplikatów, także przy równoległym utworzeniu; nadal wiele wartości jednego klucza. |
| O5 | Administracyjne listy i szczegóły produktów/wariantów. | Dostęp do szkiców/ukrytych zasobów tylko dla administratora, paginacja i zbiorcze odczyty. |
| O6 | Utworzenie produktu z pierwszym wariantem, edycja metadanych i kategorii. | Poprawny szkic bez ceny daje się utworzyć, odczytać i poprawić; częściowy graf nigdy nie pozostaje w bazie. |
| O7 | Kolejne warianty, opisy/zdjęcia, zestawy cech i ochrona tożsamości. | Da się przygotować różne konfiguracje; duplikaty i niedozwolona zmiana znaczenia istniejącego wariantu są odrzucone. |
| O8 | Administracyjny odczyt, inicjalizacja i korekta magazynu z powodem. | Nieznany stan jest różny od zera; równoległa korekta nie gubi zmian; audyt zawiera autora i ilości. |
| O9 | Podgląd gotowości, publikacja/ukrycie, daty i docelowe ograniczenia publikacji. | Pełna oferta pojawia się publicznie; niekompletna nie może zostać opublikowana; ukrycie zachowuje historię i powiązania. |
| O10 | Odbiór całego procesu, testy styku z koszykiem, wydajność i wdrożenie. | Testy i build zaliczone, migracje sprawdzone na izolowanej bazie, udokumentowany scenariusz Postmana i wynik pomiaru. |

- [x] O0 — reguły i koordynacja; rekomendacje przyjęte przy poleceniu implementacji, zachowane równoległe modele i migracja koszyka.
- [x] O1 — ograniczenia i audyt lokalnych danych; minimum wariantu, konfiguracje, cykle oraz współbieżność sprawdzone na PostgreSQL. Przed Supabase wykonać osobny raport jego danych.
- [x] O2 — wersje, dziennik i fundament zapisu.
- [x] O3 — kategorie.
- [x] O4 — słownik cech.
- [x] O5 — odczyt administracyjny.
- [x] O6 — produkt i pierwszy wariant.
- [x] O7 — kolejne warianty i ich konfiguracje.
- [x] O8 — stany magazynowe.
- [x] O9 — gotowość i publikacja.
- [ ] O10 — lokalny odbiór, aktualizacja istniejącej bazy i porównanie kodu odczytu wykonane; pozostają migracje Supabase, rzeczywisty Auth w środowisku docelowym, frontend i pomiar środowiskowy.

O3 i O4 korzystają z O2; O6 z O3–O5; O7 z O4 i O6; O8 wymaga zapisu wariantu i podstaw O2; O9 spina poprzednie etapy oraz istniejące ceny. Do O6 można już ręcznie przygotować i poprawiać szkic; dopiero O9 zamyka kompletny proces wprowadzania oferty do sprzedaży.

## 11. Testy i weryfikacja

Testy jednostkowe: generator slugów, normalizacja kodów/wartości, haszowanie ponowień, walidacja pól i kody blokad gotowości. Testy PostgreSQL: rzeczywiste ograniczenia, wyzwalacze, migracje, współbieżność i odroczone błędy przy commit. Nie zastępujemy ich EF InMemory.

Wymagane scenariusze integracyjne:

- Atomowe utworzenie szkicu z wariantem; rollback produktu, linków i dziennika po błędzie konfiguracji.
- Dwóch administratorów edytujących tę samą wersję; niezależna zmiana ceny lub stanu nie powoduje utraty zmian opisowych.
- Ponowienie utworzenia/edycji/usunięcia, inna treść pod tym samym kluczem, ponowienie po zmianie lub usunięciu kategorii.
- Dwa identyczne zestawy cech, różna kolejność ID, dwa puste zestawy, wiele wartości klucza i warianty `Hidden` nadal uwzględnione w unikalności.
- Równoległe dodawanie tej samej pary; NFC, białe znaki, wielkość liter, limit wartości i stabilność ID istniejącej pary.
- Równoczesne przeniesienia kategorii próbujące utworzyć cykl, zajęty slug rodzeństwa i usunięcie liścia z produktem.
- Publikacja bez ceny/kategorii/widocznego wariantu, ukrycie ostatniego wariantu i usunięcie ostatniej kategorii produktu `Published`.
- Ukrycie w aktywnej promocji bez kasowania historii; późniejsze zakończenie promocji nadal działa. Nowy wariant nie dołącza do zamrożonego zakresu promocji.
- Powiązania kolekcji nie są usuwane przez ukrycie produktu; nie zmieniamy automatycznie statusu kolekcji. Ich dotychczasowe warunki publikacji pozostają respektowane.
- Brak stanu, zero, niepoprawna ilość, równoległa inicjalizacja i korekta magazynu; brak rezerwacji przez operacje koszyka.
- Ukryty wariant obecny w koszyku/ulubionych pozostaje identyfikowalny jako niedostępny; brak przypadkowego usuwania danych klienta.
- Seeder po migracjach i po wykonanych operacjach administracyjnych: poprawny reset, publikacja w kolejności, wersje i liczniki.

Odbiór HTTP obejmuje rzeczywistą sesję administratora i użytkownika bez uprawnień, ochronę zapisów oraz cały przepływ od kategorii do publicznego produktu. Istniejący wzorzec testu HTTPS można wykorzystać z izolowanym lokalnym katalogiem; nie seedujemy przy tym prawdziwego sklepu ani nie dopisujemy kont/sekretów do dokumentacji.

Wydajność mierzymy na co najmniej dotychczasowym katalogu 5000 produktów i produkcie z większą liczbą wariantów: listy administracyjne, gotowość, publikacja, korekta i równoległe zapisy; liczba SQL, p50/p95 oraz oczekiwanie na globalną blokadę. Porównujemy publiczny odczyt przed/po, żeby nowe wyzwalacze nie wprowadziły regresji. Nie deklarujemy SLA bez pomiaru i nie usuwamy istniejącej globalnej blokady bez osobnej analizy.

Przed zgłoszeniem implementacji jako gotowej: `dotnet build`, odpowiednie testy i pełna regresja, sprawdzenie modelu EF względem migracji, przegląd wygenerowanego SQL i migracja na izolowanej bazie. Dopiero potem zastosowanie uzgodnionej migracji środowiskowej oraz odbiór HTTP. Nie oznaczamy całego punktu 2.5–2.7 jako zakończonego bez sprawdzenia pozostałych warunków, w tym połączenia z frontendem.

## 12. Rekomendacje przyjęte do wdrożenia

Przy poleceniu realizacji planu przyjęto poniższe rekomendacje jako zakres pierwszej implementacji. Szczegóły rzeczywistego działania i resetu są w części 13:

| Temat | Rekomendacja |
| --- | --- |
| Usuwanie oferty | W pierwszej wersji wyłącznie ukrywanie produktów/wariantów; usuwanie pustej kategorii dozwolone. |
| Tożsamość konfiguracji | Zestaw cech zmienialny tylko dla szkicu bez historii ceny/publikacji/promocji; później nowy wariant. |
| Normalizacja wartości i SKU | Wartość NFC + trim, bez lowercase; SKU trim z obecną wrażliwością na wielkość liter. |
| Data publikacji i gotowość | Pierwsza znana publikacja w `PublishedAt`; cena PLN i kategoria jako blokady, zdjęcia/opis/stan jako ostrzeżenia. |
| Ręczny stan | Ustawianie ilości fizycznej z wersją i obowiązkowym powodem; bez kasowania stanu i bez rezerwacji. |
| Limity wejścia | 20 URL-i, 2048 znaków URL, 20 000 znaków opisu, 100 par wariantu; wdrożone w walidacji API. |
| Reset developerski z koszykiem | Jawne czyszczenie pozycji z podniesieniem wersji właścicieli, bez kasowania kont/list i bez `CASCADE`, zgodnie z wdrożeniem równoległego modułu. |

Kolejność implementacji: fundament zapisu, kategorie i słownik, następnie szkic produktu, warianty, magazyn i publikacja. Warunki oraz wersje są wspólne dla tych operacji.

## 13. Wdrożenie, odbiór lokalny i użycie

W kodzie są kontrolery `AdminProductsController`, `AdminVariantsController`, `AdminCategoriesController`, `AdminAttributesController`, `AdminVariantStockController`; wspólny dostęp realizuje `AdminCatalogControllerBase`. Serwisy są w `Services/Catalog/Management/`. Dostęp pochodzi z istniejącego `AdministratorAccess`, bez przekazywania autora w body. Odpowiedzi, także błędy automatycznej walidacji MVC, mają `Cache-Control: no-store`.

Wdrożono wszystkie trasy z części 6. Metody PUT metadanych zastępują edytowany zestaw pól: w edycji wariantu brak opisu oznacza jego wyczyszczenie, brak `imageUrls` pustą tablicę. Edycja produktu/kategorii wymaga jawnego slugu, więc zmiana nazwy nie generuje nowego adresu. Limit przypisań kategorii wynosi 1000; par cech 100. Słownik par ma NFC i ustalony trim, bez zmiany wielkości liter.

Nowe migracje EF:

- `20261101133124_AddCatalogManagement`: pięć wersji, `catalog_operations`, wersjonowanie grafu i rozszerzenie globalnej blokady. `Kind` dziennika jednoznacznie określa rodzaj zasobu i operacji, więc nie ma redundantnego pola `TargetType`.
- `20261101133155_AddCatalogPublicationRules`: kontrola danych przed zmianą, kanoniczne wartości, odroczone warunki publikacji i ochrona pierwszej daty publikacji. Reguła niezmieniania konfiguracji z historią jest walidowana w serwisie; baza osobno blokuje przenoszenie wariantu do innego produktu i zmianę znaczenia współdzielonej pary/kodu klucza.

Przed migracją środowiskową uruchomić odczytowy [scripts/catalog-preflight.sql](../scripts/catalog-preflight.sql). Wiersze `blocker` wymagają jawnej korekty przed drugą migracją. `warning` o nieznanej dacie nie oznacza, że należy wymyślić datę historyczną. Migracja nie scala par ani nie ukrywa towarów automatycznie. Kod i migracje koszyka oraz oferty trzeba wdrożyć w spójnej kolejności; snapshot zawiera obydwa moduły.

Seeder czyści teraz 20 tabel: wcześniejsze 19 wraz z pozycjami zakupowymi oraz `catalog_operations`. Zachowuje właścicieli i podnosi ich wersje, gdy usuwa pozycje, zgodnie z modułem koszyka. Najpierw zapisuje graf szkiców, potem zbiorczo publikuje warianty, produkty i kolekcje; nie przeładowuje pojedynczo tysięcy rekordów. `created.catalogOperations` wynosi `0`, ponieważ seeder nie fabrykuje działań administratora. `added` pozostaje sumą liczników.

### Postman — przygotowanie i pierwsza oferta

1. Po migracjach zaloguj się kontem z wpisem w `app.administrators`; zachowaj ciasteczko sesji. Przyjmij `host` jako pełny prefiks `https://localhost:PORT/api/v1/web`.
2. Dla każdego zapisu ustaw `Content-Type: application/json`, `X-Requested-With: XMLHttpRequest`, `Idempotency-Key: {{$guid}}`. Przy ponowieniu po zerwanym połączeniu użyj zapamiętanego poprzedniego UUID i identycznego body — ponowne wygenerowanie `{{$guid}}` oznacza nową operację.
3. `POST {{host}}/admin/categories` z `{"displayName":"Koszulki"}`. Zapamiętaj `id` odpowiedzi jako `categoryId`.
4. Utwórz klucz przez `POST {{host}}/admin/attribute-keys` z `{"code":"kolor","displayName":"Kolor"}`, a wartość przez `POST {{host}}/admin/attribute-keys/{id}/values` z `{"value":"czerwony"}`. Zapamiętaj ID pary.
5. `POST {{host}}/admin/products` z poniższym body; podmień zmienne na UUID. Zapisz `id`, `variantId`, `version`, `variantVersion`.

```json
{
  "name": "Koszulka Basic",
  "slug": "koszulka-basic",
  "categoryIds": ["{{categoryId}}"],
  "firstVariant": {
    "sku": "BASIC-RED-S",
    "description": "Bawełniana koszulka.",
    "shortDescription": "Klasyczny krój.",
    "imageUrls": [],
    "attributePairIds": ["{{attributePairId}}"]
  }
}
```

6. Ustaw cenę istniejącym `POST {{host}}/admin/variants/{{variantId}}/price-changes`: `{"baseAmount":79.99,"currency":"PLN","reason":"Cena początkowa","expectedPriceChangeId":null}`.
7. `PUT {{host}}/admin/variants/{{variantId}}/stock`: `{"quantityOnHand":12,"expectedVersion":null,"reason":"Przyjęcie towaru"}`. Odpowiedź zawiera osobne `id` i `version` stanu. Kolejne korekty wymagają tej wersji; obydwa pola ilości i oczekiwanej wersji są obowiązkowe w JSON.
8. `PUT {{host}}/admin/variants/{{variantId}}/status`: `{"status":"Published","expectedVersion":{{variantVersion}}}`. Zapisz `productVersion` zwrócone po zmianie.
9. `GET {{host}}/admin/products/{{productId}}/publication-readiness`. Przy `canPublish=true` wywołaj `PUT {{host}}/admin/products/{{productId}}/status` z `{"status":"Published","expectedVersion":{{productVersion}}}`. Cena i magazyn nie zwiększają wersji opisowej produktu; zmiana wariantu zwiększa ją.
10. Sprawdź `GET {{host}}/products?id={{productId}}` i `POST {{host}}/admin/products/search` z `{"q":"BASIC-RED","status":"Published","page":1,"pageSize":20}`. Ukrycie to ten sam endpoint statusu produktu z `Hidden` i aktualną wersją.

Odpowiedź zwykłej mutacji ma `replayed`, `id`, `version` oraz, zależnie od operacji, `productId`, `productVersion`, `variantId`, `variantVersion`. Usunięcie pustej kategorii zwraca `204` bez body; ponowienie z tym samym kluczem i wersją także działa po jej usunięciu. Przy `409` odczytaj aktualne dane i podejmij nową decyzję edycyjną; nie nadpisuj automatycznie cudzej zmiany.

### Dowody i granice odbioru

Wynik końcowy 01.11.2026: **162 testy zaliczone, 0 pominiętych; build rozwiązania — 0 błędów i 0 ostrzeżeń**. Nowy zakres obejmuje 20 przypadków. Model EF nie ma zmian oczekujących poza zapisanymi migracjami. Odczytowy raport na skonfigurowanym Supabase dał **0 blokad i 0 ostrzeżeń**; potwierdzono, że obie migracje oferty nie są jeszcze zastosowane. Audyt nie zmieniał danych. Przed późniejszym wdrożeniem raport trzeba powtórzyć, jeżeli dane się zmienią.

Obie migracje zastosowano na osobnym lokalnym PostgreSQL 17. Testy `CatalogManagementPostgresTests` obejmują wersje, ponowienia, wycofanie dziennika po błędzie przy commit, słownik, cykle, współbieżność i stan. `CatalogManagementHttpTests` przechodzą przez routing, JSON, rzeczywiste cookies aplikacji i kontrolę tabeli administratorów; zewnętrzne odpowiedzi Supabase Auth są atrapą. Test łączy nową ofertę z koszykiem/ulubionymi drugiego modułu i sprawdza niedostępność po ukryciu bez usuwania pozycji.

Dawne testy importowanej oferty USD/przyszłej ceny korzystają wyłącznie w izolowanym fixture z uprzywilejowanego wyłączenia nowych ograniczeń publikacji. Ograniczenia grafu nadal są wykonywane, a nowe wyzwalacze są przywracane w tej samej transakcji. API nie zawiera takiego obejścia i nie publikuje nowych wariantów bez bieżącej ceny PLN.

Pomiar 5001 produktów, 15 100 wariantów i produktu ze 100 wariantami opisuje [audyt wydajności](catalog-performance-audit.md). Obejmuje rzeczywisty EF i PostgreSQL, bez HTTP/Auth. Dodatkowe porównanie wcześniejszego i aktualnego kodu na tej samej bazie zachowało skróty odpowiedzi i nie wykazało regresji tych odczytów; nie jest pomiarem Supabase ani porównaniem dwóch schematów bazy. Przed wdrożeniem środowiskowym pozostają elementy O10 wskazane na liście powyżej.

Skrypt `tests/performance/catalog-management/verify-migrations.ps1` tworzy dodatkową lokalną bazę `ecomm_test_offer_migrations_*` i sprawdza aktualizację od stanu przed administracją, odrzucenie obu niezgodnych fixture, zachowanie pełnego skrótu historii cen oraz Down/Up publikacji. Zachowuje też nieznane `PublishedAt`, zarówno przy migracji, jak i zwykłej późniejszej edycji metadanych. Wymaga lokalnego PostgreSQL, `ECOMMERCE_TEST_DATABASE` i uprawnień do utworzenia osobnej bazy. Nie dotyka bazy podanej w zmiennej poza użyciem jej adresu serwera do stworzenia nowej.

## Dokumenty powiązane

- [Plan backendu](project-direction.md), [model katalogu](catalog-model.md), [model magazynu](inventory-model.md).
- [Model cen](pricing-model.md), [promocje](promotion-implementation-plan.md), [kolekcje](collection-implementation-plan.md).
- [Koszyk i ulubione — równoległy plan](cart-favorites-implementation-plan.md).
- [Uprawnienia administratora](administrator-access.md), [kontrakt API](api-contract.md), [baza danych](database.md), [testy](testing.md).
- [Utrzymanie seedera](seeder-maintenance.md), [seeder](demo-seed.md), [dotychczasowy pomiar katalogu](catalog-performance-audit.md).

Kontrola `protected-area-check` podczas planowania: lista w [protected-areas.md](protected-areas.md) jest pusta. Przed przyszłą implementacją sprawdzić ją ponownie, zgodnie z instrukcjami repozytorium.
