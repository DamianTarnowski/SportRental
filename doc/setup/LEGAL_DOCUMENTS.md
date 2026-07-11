# Dokumenty prawne aplikacji klienckiej

Aplikacja WASM udostępnia:

- `/terms` — regulamin usług elektronicznych platformy RentSpot,
- `/privacy` — politykę prywatności platformy.

Checkout dodatkowo pobiera i pokazuje regulamin każdej wypożyczalni. Jeżeli właściciel
nie uzupełnił pola `CompanyInfo.RegulationsText`, API stosuje wersjonowany wzorzec
`DefaultRentalRegulations`. Hash oraz treść zaakceptowanej wersji są zapisywane przy
konkretnym `Rental`, dzięki czemu późniejsza edycja ustawień firmy nie zmienia dowodu
akceptacji ani treści wysyłanej w potwierdzeniu.

Dokumenty dotyczą Operatora RentSpot. Nie zastępują danych, obowiązków informacyjnych,
regulaminu ani warunków umowy konkretnej wypożyczalni.

## Publiczne dane Operatora

Przed wdrożeniem produkcyjnym skonfiguruj poniższe klucze. To dane publiczne, nie sekrety:

```text
Legal:ServiceName
Legal:OperatorName
Legal:OperatorAddress
Legal:OperatorNip
Legal:OperatorKrs
Legal:OperatorEmail
Legal:OperatorPhone
Legal:ComplaintsEmail
Legal:PrivacyEmail
```

Do oznaczenia konfiguracji jako kompletnej potrzebne są nazwa, adres, e-mail, telefon oraz
co najmniej NIP albo KRS. Brak tych danych powoduje wyświetlenie jawnego alertu o wersji
roboczej — aplikacja nie podstawia fikcyjnych danych.

## Wersjonowanie i dowód akceptacji

Aktualne wersje oraz datę obowiązywania określa
`SportRental.Shared/Legal/LegalDocumentVersions.cs`. Przy istotnej zmianie dokumentu:

1. ustaw nową wersję i datę,
2. sprawdź oba dokumenty z prawnikiem obsługującym polski e-commerce i ochronę danych,
3. przetestuj rejestrację, logowanie Google i checkout,
4. wdroż migrację bazy przed udostępnieniem nowej wersji klienta.

Rejestracja zapisuje wersję regulaminu, wersję potwierdzonej polityki i czas w `AspNetUsers`.
Checkout zapisuje te same dane w `CheckoutSessions`, dzięki czemu obejmuje także sesję gościa.
Polityka prywatności jest potwierdzana jako informacja — checkbox nie jest ogólną zgodą na
każdy cel przetwarzania.

Gość może odzyskać dostęp pod `/guest-access`, podając adres z zamówienia i jego publiczny
numer. Serwer wysyła jednorazowy link ważny 20 minut; w bazie przechowywany jest wyłącznie
SHA-256 tokenu. Odpowiedź endpointu żądania jest jednakowa dla danych pasujących i
niepasujących, aby nie ujawniać istnienia zamówień.

W środowisku innym niż Development trzeba ustawić zaufany publiczny adres HTTPS w
`ClientApp:PublicBaseUrl` albo `Admin:PublicBaseUrl`. Linki odzyskiwania i redirecty Stripe
nie korzystają z nagłówka `Host`; przy braku poprawnej konfiguracji nie są generowane.

## Kontrola przed produkcją

- uzupełnij prawdziwe dane Operatora i działające kanały reklamacji/prywatności,
- zweryfikuj treść z polskim prawnikiem oraz faktyczne umowy z dostawcami danych,
- zapewnij procedurę realizacji praw RODO i retencji danych,
- upewnij się, że każda wypożyczalnia przekazuje własne obowiązkowe informacje i warunki
  konkretnego wynajmu we właściwym momencie przed związaniem konsumenta umową,
- po zmianach dostawców, profilowania, płatności lub pamięci przeglądarki zaktualizuj politykę.
