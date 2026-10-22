# Utrzymanie seedera przy zmianach bazy

Cel: zapewnić, że rozwój modelu danych aplikacji jest uwzględniany w ręcznie uruchamianym seederze demonstracyjnym. Sposób uruchomienia i obecny zakres seedera opisuje [demo-seed.md](demo-seed.md).

## Stan obecny

`DemoCatalogSeeder` tworzy przykładowy katalog w tabelach kategorii, produktów, wariantów, cech, przypisań, stanów i historii cen. Endpoint jest dostępny tylko w środowisku developerskim po włączeniu flagi i podaniu klucza. Każde wywołanie najpierw czyści dziewięć tabel katalogowych, także z danymi spoza seedera, a następnie tworzy nowy zestaw w tej samej transakcji. Tabele kont, profili i uwierzytelniania pozostają nietknięte. Pełny zakres i ograniczenia opisuje [demo-seed.md](demo-seed.md).

## Zasada przy kolejnych zmianach

Decyzja użytkownika z 22.10.2026: kiedy rozwój aplikacji dodaje lub zmienia dane w bazie, w tym samym zadaniu należy sprawdzić i odpowiednio rozbudować seeder. Dotyczy to nowych tabel, wymaganych pól, relacji, ograniczeń oraz nowych modułów potrzebnych do demonstracji działania sklepu.

Przy takiej zmianie:

1. Ustal, jakie poprawne dane przykładowe są potrzebne dla nowego modelu i czy powinny powstawać na poziomach `low`, `medium` i `high`. Zachowaj opcjonalność pól body endpointu.
2. Dodaj dane do grafu tworzonego w pamięci, ustawiając UUID i powiązania przed zapisem. Zachowaj dwa etapowe zapisy (szkice z danymi zależnymi, potem publikacja) oraz atomowość transakcji; nie przywracaj zapytania i zapisu dla każdego rekordu.
3. Rozszerz jawny wykaz tabel czyszczonych przed seedowaniem, jeśli nowa tabela należy do katalogu lub scenariusza demo. Nie obejmuj nim tabel kont, profili ani `auth`. Reset musi pozostać w transakcji i bez `CASCADE`, aby nowe zależności wymagały świadomej decyzji. Nie używaj tego endpointu na bazie z rzeczywistą historią cen potrzebną do wymogu Omnibus.
4. Rozszerz odpowiedź `created` o licznik nowego typu danych i dopilnuj, aby `added` pozostało sumą wszystkich liczników. Zaktualizuj przykłady i opis poziomów w [demo-seed.md](demo-seed.md) oraz kontrakt w [api-contract.md](api-contract.md).
5. Dodaj odpowiednie testy reguł lub mapowania. Gdy poprawność zależy od ograniczeń PostgreSQL, sprawdź także zapis i ponowne wywołanie na izolowanej bazie testowej; nie uznawaj samego builda za potwierdzenie zapisu.

Seeder nie powinien tworzyć kont klientów ani danych wymagających zewnętrznej usługi tylko dlatego, że pojawiła się nowa tabela. Jeśli nowego obszaru nie da się sensownie lub bezpiecznie seedować, zapisz powód i sposób ręcznego przygotowania danych w dokumentacji tego obszaru. Nie przedstawiaj wtedy jego pokrycia przez seeder jako ukończonego.
