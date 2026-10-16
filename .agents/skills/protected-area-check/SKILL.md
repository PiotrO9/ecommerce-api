---
name: protected-area-check
description: Sprawdź, czy plan lub wdrożenie zmiany w tym repozytorium dotyka obszaru chronionego; użyj przy planowaniu zmian technicznych i ponownie przed ich wdrożeniem.
---

# Kontrola obszarów chronionych

Przeczytaj `docs/protected-areas.md`. Porównaj z nim planowane zachowanie, przewidywane pliki i zależności, a przed wdrożeniem także aktualny kod i rzeczywisty zakres zmian. Oceniaj skutki pośrednie, nie tylko nazwy katalogów. Nie zakładaj, że obszar jest chroniony wyłącznie dlatego, że zadanie go wspomina.

## Podczas planowania

Jeśli plan dotyka obszaru chronionego, przygotuj konkretny plan możliwy do oceny: wskaż obszar, proponowane pliki lub zachowania, cel zmiany oraz możliwy wpływ na dostęp lub bezpieczeństwo. Powiedz użytkownikowi, że wymóg potwierdzenia wynika z tego skilla i `docs/protected-areas.md`. Poproś o wyraźne potwierdzenie objęcia tego obszaru planem. Nie traktuj ogólnej prośby o wykonanie zadania jako takiego potwierdzenia.

## Przed wdrożeniem

Sprawdź ponownie aktualny plan, kod i przewidywany zakres edycji. Jeśli zmiana dotknie obszaru chronionego, przedstaw dokładnie, co zamierzasz zmienić i dlaczego, oraz poproś o **ponowne, odrębne potwierdzenie przed pierwszą edycją tego obszaru**. Wcześniejsze potwierdzenie planu nie wystarcza. Jeśli podczas pracy wyjdzie na jaw nowy obszar chroniony lub istotnie szerszy wpływ, zatrzymaj edycje tego obszaru i ponownie przedstaw rozszerzony zakres do potwierdzenia.

Potwierdzenie dotyczy wyłącznie opisanego zakresu w bieżącym zadaniu. Brak odpowiedzi, milczenie ani upływ czasu nie oznaczają zgody. Bez potwierdzenia nie edytuj chronionego obszaru; możesz kontynuować niezależne prace poza nim.

Jeśli nie stwierdzisz wpływu na obszar chroniony, pracuj zwykłym trybem bez dodatkowego pytania. Nie obiecuj, że sama kontrola zastępuje testy lub przegląd kodu.
