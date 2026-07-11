namespace SportRental.Shared.Legal;

/// <summary>
/// Awaryjny, platformowy wzorzec zasad wynajmu pokazywany wtedy, gdy
/// wypożyczalnia nie opublikowała własnego regulaminu. Nie zastępuje danych
/// przedsiębiorcy ani dodatkowych warunków przekazanych przy konkretnej ofercie.
/// </summary>
public static class DefaultRentalRegulations
{
    public const string Version = "2026-07-10.1";

    public const string Content = """
        STANDARDOWY REGULAMIN WYPOŻYCZALNI RENTSPOT

        1. Strony i zakres
        Umowa wynajmu sprzętu jest zawierana z wypożyczalnią wskazaną przy danej ofercie i w podsumowaniu rezerwacji. RentSpot zapewnia obsługę techniczną katalogu, rezerwacji i płatności, ale nie jest właścicielem sprzętu. Jeżeli zamówienie obejmuje kilka wypożyczalni, dla każdej z nich powstaje osobna rezerwacja i osobna umowa wynajmu.

        2. Rezerwacja
        Rezerwacja obejmuje wyłącznie sprzęt, ilość, termin, punkt odbioru i kwoty pokazane w podsumowaniu. Dodanie produktu do koszyka lub czasowa blokada dostępności nie jest jeszcze potwierdzeniem wynajmu. Rezerwacja zostaje potwierdzona po pomyślnym zakończeniu checkoutu i otrzymaniu potwierdzenia.

        3. Cena i kaucja
        Cena wynajmu oraz zwrotna kaucja są podawane przed złożeniem zamówienia. Kaucja nie jest zapłatą za wynajem. Wypożyczalnia może potrącić z niej wyłącznie należności wynikające z umowy, w szczególności za niezwrócony sprzęt, udokumentowane uszkodzenie powstałe z przyczyn leżących po stronie klienta lub uzgodnione opóźnienie. Rozliczenie nie ogranicza praw klienta wynikających z bezwzględnie obowiązujących przepisów.

        4. Odbiór sprzętu
        Klient odbiera sprzęt w punkcie i godzinach wskazanych w podsumowaniu. Przy odbiorze należy sprawdzić kompletność i widoczny stan sprzętu oraz zgłosić zastrzeżenia obsłudze. Wypożyczalnia może poprosić o potwierdzenie tożsamości lub uprawnienia wymagane do bezpiecznego używania danego sprzętu, jeżeli poinformowała o tym przed zawarciem umowy albo wynika to z prawa.

        5. Korzystanie ze sprzętu
        Sprzętu należy używać zgodnie z przeznaczeniem, instrukcją, zasadami bezpieczeństwa i warunkami pogodowymi. Bez zgody wypożyczalni nie wolno oddawać go osobie trzeciej, dokonywać napraw, przeróbek ani usuwać oznaczeń. Awarię, wypadek, utratę lub kradzież należy niezwłocznie zgłosić wypożyczalni i postępować według jej uzasadnionych instrukcji.

        6. Zwrot
        Sprzęt należy zwrócić w uzgodnionym miejscu i terminie, w stanie odpowiadającym prawidłowemu używaniu, wraz ze wszystkimi elementami. Zwykłe zużycie nie jest uszkodzeniem. Opóźnienie, brak elementów lub uszkodzenie mogą zostać rozliczone według zasad i stawek przekazanych klientowi przed zawarciem umowy, z uwzględnieniem rzeczywistego przebiegu zdarzenia.

        7. Zmiana i anulowanie
        Możliwość zmiany albo anulowania rezerwacji, związane terminy i ewentualne koszty są pokazywane w podsumowaniu lub uzgadniane z wypożyczalnią. Prawa konsumenta do odstąpienia od umowy zawartej na odległość ocenia się z uwzględnieniem rodzaju usługi, wskazanego terminu realizacji i obowiązujących wyjątków ustawowych. Niniejszy regulamin nie wyłącza praw, których nie można wyłączyć umową.

        8. Reklamacje
        Reklamacje dotyczące stanu sprzętu, odbioru, zwrotu, ceny, kaucji lub wykonania wynajmu należy kierować do wypożyczalni wskazanej w rezerwacji. Zgłoszenie powinno zawierać numer rezerwacji, opis zdarzenia i oczekiwane rozwiązanie. Problemy techniczne konta, checkoutu lub działania platformy można zgłaszać Operatorowi RentSpot.

        9. Dane osobowe
        Dane klienta są przekazywane właściwej wypożyczalni w zakresie potrzebnym do obsługi wynajmu. Role administratorów, cele, podstawy, odbiorcy, okresy przechowywania i prawa użytkownika opisuje Polityka prywatności RentSpot oraz informacja przekazana przez wypożyczalnię.

        10. Postanowienia końcowe
        Ten standardowy regulamin obowiązuje tylko wtedy, gdy dana wypożyczalnia nie opublikowała własnego regulaminu. Dane wypożyczalni, oferta, podsumowanie rezerwacji i indywidualnie uzgodnione warunki uzupełniają jego treść. Sprzeczne postanowienie nie narusza bezwzględnie obowiązujących praw konsumenta.
        """;
}
