# Testy backendu

Cel: opisać układ testów i sposób ich uruchamiania w tym repozytorium.

## Stan potwierdzony

- Rozwiązanie `E-commerce-backend.slnx` zawiera aplikację API oraz projekt xUnit `tests/E-commerce-backend.Tests`.
- Projekt API wyklucza cały katalog `tests/` ze swoich elementów kompilacji i zawartości, ponieważ leży w katalogu głównym repozytorium.
- Projekt testowy zawiera xUnit, uruchamiany poleceniem `dotnet test E-commerce-backend.slnx`.
- `Examples/Calculator.cs` zawiera demonstracyjną funkcję `Add`. `Unit/CalculatorTests.cs` testuje ją bez uruchamiania API.
- Funkcja demonstracyjna nie jest używana przez endpoint `GET /` i nie stanowi logiki biznesowej sklepu.

## Zasada na przyszłość

Gdy pojawią się reguły biznesowe, dodajemy ich testy jednostkowe do projektu testowego. Test powinien wywoływać kod bez uruchamiania serwera HTTP i bez zależności od bazy danych lub innych usług.

Gdy AI tworzy testy, powinno sprawdzać zarówno poprawne wyniki, jak i oczekiwane niepowodzenia oraz istotne przypadki brzegowe. Dla scenariusza błędnego należy potwierdzić konkretne zachowanie wynikające z kontraktu kodu, np. właściwy wyjątek lub wynik oznaczający błąd. Nie ograniczamy zestawu testów do ścieżki pomyślnej i nie wymyślamy zachowań błędnych, których aplikacja nie definiuje.
