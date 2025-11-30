# Integracja z SMSAPI

## Wprowadzenie

Projekt SportRental wykorzystuje [SMSAPI.pl](https://www.smsapi.pl) do wysyłania wiadomości SMS do klientów. Integracja umożliwia:

- Wysyłanie powiadomień SMS o wynajmach
- Wysyłanie przypomnień o zbliżającym się terminie zwrotu
- Wysyłanie podziękowań po wypożyczeniu
- Obsługę raportów doręczeń (delivery reports / callbacks)

## Konfiguracja

### Zmienne konfiguracyjne

Konfiguracja SMSAPI znajduje się w pliku `appsettings.json` w sekcji `smsApi`:

```json
{
  "smsApi": {
    "authToken": "TWÓJ_TOKEN_OAUTH",
    "isEnabled": true,
    "sendConfirmationAttempts": 5,
    "senderName": "Test"
  }
}
```

| Parametr | Opis | Wartość domyślna |
|----------|------|------------------|
| `authToken` | Token OAuth do autoryzacji w SMSAPI. Generowany w panelu [SMSAPI → Tokeny API](https://ssl.smsapi.pl/react/oauth/manage) | - |
| `isEnabled` | Flaga włączająca/wyłączająca wysyłanie SMS. Gdy `false`, wiadomości są logowane do konsoli. | `false` |
| `sendConfirmationAttempts` | Liczba prób ponownego wysłania SMS w przypadku błędu API (z 2-sekundowym opóźnieniem między próbami) | `5` |
| `senderName` | Nazwa nadawcy SMS (pole "from" w SMSAPI, max 11 znaków). Musi być zarejestrowane w panelu SMSAPI. | `Test` |

### Uzyskanie tokena OAuth

1. Zaloguj się do panelu [SMSAPI](https://ssl.smsapi.pl)
2. Przejdź do **Ustawienia → Tokeny API**
3. Kliknij **Dodaj nowy token**
4. Nadaj uprawnienia: `sms:send` (wysyłanie SMS)
5. Skopiuj wygenerowany token do `appsettings.json`

### Rejestracja nazwy nadawcy

Domyślna nazwa nadawcy to `Test`. Aby używać własnej nazwy:

1. W panelu SMSAPI przejdź do **Ustawienia → Pola nadawcy**
2. Dodaj nowe pole nadawcy (max 11 znaków alfanumerycznych)
3. Poczekaj na weryfikację (zwykle do 24h)
4. Zaktualizuj `senderName` w konfiguracji

## Architektura

### Komponenty

```
SportRental.Admin/Services/Sms/
├── ISmsSender.cs              # Interfejs wysyłania SMS
├── SmsApiSender.cs            # Implementacja z SMSAPI
├── SmsApiSettings.cs          # Model konfiguracji
├── SmsDeliveryReport.cs       # Model raportu doręczenia
├── SmsConfirmationService.cs  # Serwis kodów potwierdzających
└── ConsoleSmsSender.cs        # Fallback do konsoli (dev)

SportRental.Admin/Api/
└── SmsApiCallbackEndpoints.cs # Endpoint dla callbacków SMSAPI
```

### Interfejs ISmsSender

```csharp
public interface ISmsSender
{
    Task SendAsync(string phoneNumber, string message, CancellationToken ct = default);
    Task SendThanksMessageAsync(string phoneNumber, string customerName, string? customMessage = null, CancellationToken ct = default);
    Task SendReminderAsync(string phoneNumber, string customerName, string? customMessage = null, CancellationToken ct = default);
    Task SendConfirmationRequestAsync(string phoneNumber, string customerName, Guid rentalId, CancellationToken ct = default);
}
```

### Normalizacja numeru telefonu

Numery telefonów są automatycznie normalizowane przed wysłaniem:
- Usuwany jest prefix `+48` lub `48`
- Usuwane są spacje, myślniki i nawiasy
- Przykład: `+48 123-456-789` → `123456789`

### Retry Logic

W przypadku błędu API, system automatycznie ponawia próby wysłania:
- Maksymalna liczba prób: `sendConfirmationAttempts` (domyślnie 5)
- Opóźnienie między próbami: 2 sekundy
- Po wyczerpaniu prób rzucany jest `InvalidOperationException`

## Endpoint Callback (Raporty doręczeń)

SMSAPI może wysyłać raporty doręczeń (delivery reports) na wskazany URL.

### Konfiguracja w panelu SMSAPI

1. Przejdź do **Ustawienia → Adresy URL callback**
2. Dodaj URL: `https://TWOJA_DOMENA/api/sms/callback`
3. Wybierz format: **POST** lub **GET**

### Endpoint

```
POST /api/sms/callback
GET  /api/sms/callback (test/weryfikacja)
```

### Parametry callbacka

| Parametr | Opis |
|----------|------|
| `MsgId` | Unikalny identyfikator wiadomości |
| `status` | Status doręczenia: `DELIVERED`, `UNDELIVERED`, `EXPIRED`, `SENT`, `UNKNOWN`, `REJECTED`, `PENDING` |
| `to` | Numer telefonu odbiorcy |
| `donedate` | Data i czas zdarzenia (format: YYYY-MM-DD HH:MM:SS) |
| `idx` | Wartość parametru idx przekazanego podczas wysyłki |
| `username` | Nazwa użytkownika wysyłającego |
| `parts` | Ilość części SMS |

### Przykład odpowiedzi

Endpoint zawsze zwraca `200 OK` - wymagane przez SMSAPI.

## Tryb deweloperski

Gdy `isEnabled = false`, wszystkie wiadomości SMS są:
1. Logowane do konsoli z prefixem `[SMS-DISABLED]`
2. Rejestrowane w logach aplikacji

Przykład wyjścia konsoli:
```
[SMS-DISABLED] 123456789: Dziękujemy Jan Kowalski za wypożyczenie sprzętu w SportRental!
```

## Testy jednostkowe

Testy znajdują się w:
```
SportRental.Admin.Tests/Services/Sms/SmsApiSenderTests.cs
```

Uruchomienie testów:
```bash
dotnet test SportRental.Admin.Tests --filter "FullyQualifiedName~SmsApiSenderTests"
```

## Biblioteka SMSAPI C#

Projekt używa oficjalnej biblioteki [smsapi-csharp-client](https://github.com/smsapi/smsapi-csharp-client):

```bash
dotnet add package SMSAPI.pl
```

### Przykład użycia (wewnętrzna implementacja)

```csharp
using SMSApi.Api;

var client = new ClientOAuth("TWÓJ_TOKEN");
var smsFactory = new SMSFactory(client);

var response = await smsFactory.ActionSend()
    .SetText("Treść wiadomości")
    .SetTo("123456789")
    .SetSender("Test")
    .Execute();
```

## Limity i koszty

- SMSAPI ma limit 100 zapytań na sekundę per IP
- Koszt SMS zależy od typu wiadomości i operatora
- Szczegóły cennika: [smsapi.pl/cennik](https://www.smsapi.pl/cennik)

## Rozwiązywanie problemów

### SMS nie są wysyłane

1. Sprawdź czy `isEnabled = true`
2. Sprawdź czy `authToken` jest poprawny
3. Sprawdź logi aplikacji pod kątem błędów SMSAPI
4. Zweryfikuj saldo punktów w panelu SMSAPI

### Błąd "Sender name not registered"

Nazwa nadawcy (`senderName`) musi być zarejestrowana i zweryfikowana w panelu SMSAPI.

### Błąd "Invalid phone number"

Upewnij się, że numer jest w formacie polskim (9 cyfr bez prefiksu).

## Dokumentacja SMSAPI

- [Dokumentacja API](https://www.smsapi.pl/docs)
- [GitHub - biblioteka C#](https://github.com/smsapi/smsapi-csharp-client)
- [Panel klienta](https://ssl.smsapi.pl)

## Changelog

### v1.0.0 (2025-11-30)
- Implementacja integracji z SMSAPI.pl
- Retry logic dla wysyłania SMS
- Endpoint callback dla raportów doręczeń
- Normalizacja numerów telefonów
- Feature flag `isEnabled`

