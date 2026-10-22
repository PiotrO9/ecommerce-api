# Testy backendu

Cel: opisać układ testów i sposób ich uruchamiania w tym repozytorium.

## Stan potwierdzony

- Rozwiązanie `E-commerce-backend.slnx` zawiera aplikację API oraz projekt xUnit `tests/E-commerce-backend.Tests`.
- Projekt API wyklucza cały katalog `tests/` ze swoich elementów kompilacji i zawartości, ponieważ leży w katalogu głównym repozytorium.
- Projekt testowy zawiera xUnit, uruchamiany poleceniem `dotnet test E-commerce-backend.slnx`.
- `Examples/Calculator.cs` zawiera demonstracyjną funkcję `Add`. `Unit/CalculatorTests.cs` testuje ją bez uruchamiania API.
- `Unit/AuthLoginTests.cs` sprawdza wystawienie chronionego ciasteczka, oba terminy sesji, limit rozmiaru i odczyt po ponownym utworzeniu Data Protection. `Unit/AuthEndpointTests.cs` sprawdza zachowanie `/me` i `/logout` z atrapą odpowiedzi Supabase, w tym odświeżenie pary tokenów bez przesunięcia terminu, błędy usługi, nieważny refresh token i dwa równoległe żądania. Testy nie wysyłają żądań do prawdziwego projektu Supabase.
- `Unit/AuthLoginTests.cs` sprawdza także mapowanie rzeczywistego formatu odpowiedzi `invalid_credentials`; `Unit/AuthRegisterErrorTests.cs` pilnuje tego samego formatu dla `weak_password` przy rejestracji.
- `Unit/AuthEndpointTests.cs` sprawdza również `firstName` i `lastName` w `/auth/me`: `null` przed utworzeniem profilu, zapisane wartości po jego utworzeniu oraz brak ujawnienia profilu innego UUID. Odczyt profilu jest w testach atrapą; nie sprawdzają one połączenia z prawdziwą bazą.
- `Unit/UserProfileModelTests.cs` sprawdza mapowanie klucza głównego i obcego profilu do `auth.users`. Wygenerowany skrypt migracji został sprawdzony lokalnie; testy nie zastępują sprawdzenia migracji i uprawnień w prawdziwej bazie.
- `Unit/CatalogModelTests.cs` sprawdza mapowanie relacji wariantu, ceny i stanu, indeksy kategorii, klucze tabel łączących oraz normalizację kodu cechy. Testy metadanych nie wykonują wyzwalaczy migracji; wymagają one osobnej próby na PostgreSQL.
- `Unit/DemoSeedAccessTests.cs` sprawdza rozpoznawanie poprawnego i błędnego klucza endpointu seedującego. `Unit/DemoSeedOptionsTests.cs` sprawdza zakresy domyślnych i niezależnych poziomów, granice losowania oraz odrzucanie niepoprawnych wartości. `Unit/DemoCatalogSeedDataTests.cs` sprawdza liczniki, relacje i unikalność zestawów wariantów dla najmniejszego i największego zestawu danych. Reset dziewięciu tabel i zapis wymagają osobnego testu na izolowanym PostgreSQL; instrukcja jest w [demo-seed.md](demo-seed.md).
- `Unit/ProfileEndpointTests.cs` sprawdza, że odczyt i zapis profilu bez sesji kończą się przed wywołaniem Supabase lub PostgreSQL, a zapis wymaga nagłówka chroniącego przed prostym żądaniem CSRF.
- `Unit/PasswordResetTests.cs` sprawdza wysłanie linku, nieujawnianie braku konta, kolejność weryfikacji tokenu i zmiany hasła oraz format żądania do Mailtrap. Testy używają atrap HTTP; nie potwierdzają działania konta Mailtrap ani ustawień Supabase.
- Funkcja demonstracyjna nie jest używana przez endpoint `GET /` i nie stanowi logiki biznesowej sklepu.

## Zasada na przyszłość

Gdy pojawią się reguły biznesowe, dodajemy ich testy jednostkowe do projektu testowego. Test powinien wywoływać kod bez uruchamiania serwera HTTP i bez zależności od bazy danych lub innych usług.

Gdy AI tworzy testy, powinno sprawdzać zarówno poprawne wyniki, jak i oczekiwane niepowodzenia oraz istotne przypadki brzegowe. Dla scenariusza błędnego należy potwierdzić konkretne zachowanie wynikające z kontraktu kodu, np. właściwy wyjątek lub wynik oznaczający błąd. Nie ograniczamy zestawu testów do ścieżki pomyślnej i nie wymyślamy zachowań błędnych, których aplikacja nie definiuje.
