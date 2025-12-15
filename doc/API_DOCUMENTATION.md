# SportRental API Documentation

> **Ostatnia aktualizacja:** Grudzień 2025

## ⚠️ Aktualna architektura

**WAŻNE:** API jest obecnie hostowane w projekcie **SportRental.Admin** (Blazor Server).
Projekt **SportRental.Api** jest wyłączony - przygotowany na przyszłość.

## Podstawowe informacje
- **Base URL (dev):** `http://localhost:5001` (SportRental.Admin)
- **Base URL (prod):** `https://sradmin2.azurewebsites.net`
- **Wersja:** v1 (minimal API)
- **Naglowek tenant:** wszystkie zapytania, poza `GET /`, `OPTIONS` i `swagger`, wymagaja `X-Tenant-Id: <guid>`.
- **Format danych:** JSON UTF-8 (camelCase).
- **Uwierzytelnianie:** Cookie-based auth dla klienta WASM. Rejestracja/logowanie przez `/api/auth/register` i `/api/auth/login`.
- **Płatności:** Integracja Stripe (Checkout Sessions + Webhooks).
- **Email:** Powiadomienia o wynajmie z załącznikami PDF (kontrakty).
- **SMS:** Powiadomienia przez SMSAPI.pl.
- **Media storage:** Azure Blob Storage - pliki przechowywane bezpośrednio w Azure.
- **Sekrety:** Wszystkie dane wrażliwe (connection strings, API keys) przechowywane w Azure Key Vault - zero hardcoded secrets w kodzie!

## Wspolne naglowki
| Naglowek | Wymagany | Opis |
| --- | --- | --- |
| `X-Tenant-Id` | Tak | Guid identyfikujacy wypozyczalnie. Determinuje dane filtrowane w bazie. |
| `X-Request-Id` | Nie | Opcjonalny identyfikator klienta/klienta WASM do korelacji logow. |
| `Authorization` | Nie | Pole do przyszlej integracji (JWT/bearer). |

## Modele
- `ProductDto`: `id`, `name`, `sku`, `category`, `imageUrl`, `fullImageUrl`, `description`, `dailyPrice`, `hourlyPrice`, `isAvailable`, `availableQuantity`, `city`, `voivodeship`.
- `CreateHoldRequest`: `productId`, `quantity`, `startDateUtc`, `endDateUtc`, `ttlMinutes?`, `customerId?`, `sessionId?`.
- `CreateHoldResponse`: `id`, `expiresAtUtc`.
- `CreateRentalRequest`: `customerId`, `startDateUtc`, `endDateUtc`, `items[] { productId, quantity }`, `paymentIntentId`, `notes?`, `idempotencyKey?`.
- `RentalResponse`: `id`, `totalAmount`, `depositAmount`, `paymentStatus`, `contractUrl`, `status`.
- `PaymentQuoteRequest`: `startDateUtc`, `endDateUtc`, `items[] { productId, quantity }`.
- `PaymentQuoteResponse`: `totalAmount`, `depositAmount`, `currency`, `rentalDays`.
- `PaymentIntentDto`: `id`, `amount`, `depositAmount`, `currency`, `status`, `createdAtUtc`, `expiresAtUtc`.
- `MyRentalDto`: `id`, `title`, `startDateUtc`, `endDateUtc`, `quantity`, `totalAmount`, `depositAmount`, `paymentStatus`, `status`, `canCancel`.

## Endpointy
### GET /
- **Opis:** prosty endpoint diagnostyczny.
- **Naglowki:** brak wymaganych.
- **Odpowiedz:** `200 OK` z tekstem `"SportRental API"`.

### GET /api/products
- **Opis:** lista produktow dostepnych dla danego tenanta.
- **Naglowki:** `X-Tenant-Id` (wymagany).
- **Parametry:** brak (filtrowanie/paginacja planowana).
- **Odpowiedz 200:** tablica `ProductDto`.
```http
GET /api/products HTTP/1.1
Host: localhost:7142
X-Tenant-Id: 00000000-0000-0000-0000-000000000000
```
```json
[
  {
    "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "name": "Narty Rossignol React",
    "sku": "SKI-ROSS-001",
    "category": "Narty",
    "imageUrl": "https://cdn.local/files/images/products/.../w800.jpg",
    "fullImageUrl": "https://cdn.local/files/images/products/.../w800.jpg",
    "description": "All-mountain, dlugosc 168 cm",
    "dailyPrice": 120.0,
    "hourlyPrice": 25.0,
    "isAvailable": true,
    "availableQuantity": 6,
    "city": "Zakopane",
    "voivodeship": "małopolskie"
  }
]
```

### GET /api/products/{id}
- **Opis:** szczegoly produktu.
- **Statusy:** `200 OK`, `404 Not Found` (gdy brak w danym tenancie).

### POST /api/products/{id}/image
- **Opis:** upload zdjecia produktu do Azure Blob Storage (multipart).
- **Naglowki:** `X-Tenant-Id`, `Content-Type: multipart/form-data`.
- **Body:** pole `file` (obowiazkowe). Obslugiwane rozszerzenia: `.jpg`, `.jpeg`, `.png`, `.webp`.
- **Odpowiedz 200:** `{ "imageUrl": "...", "basePath": "images/products/..." }`.
- **Bledy:** `400` (brak pliku/niepoprawne rozszerzenie), `404` (produkt nie istnieje).

### GET /api/tenants/locations
- **Opis:** lista lokalizacji wypożyczalni z koordynatami GPS (dla mapy).
- **Naglowki:** brak wymaganych.
- **Odpowiedz 200:** tablica obiektów lokalizacji.
```json
[
  {
    "tenantId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "tenantName": "Ski Rental Zakopane",
    "lat": 49.2992,
    "lon": 19.9496,
    "address": "ul. Krupówki 10",
    "city": "Zakopane",
    "voivodeship": "małopolskie",
    "phoneNumber": "+48 123 456 789",
    "email": "kontakt@skirental.pl",
    "openingHours": "8:00-20:00",
    "logoUrl": "https://blob.../logo.png"
  }
]
```

### POST /api/tenants/{id}/logo
- **Opis:** upload logotypu firmy. Nie wymaga `X-Tenant-Id` (identyfikacja po id tenanta).
- **Zasady:** analogiczne do zdjec produktow, dopuszcza rowniez `.svg`.

### POST /api/customers
- **Opis:** tworzy nowego klienta dla bie��cego tenanta (wykorzystywane m.in. przez rejestracj� w kliencie WASM).
- **Nag��wki:** X-Tenant-Id.
- **Body:** CreateCustomerRequest (imi� i nazwisko, email, telefon oraz opcjonalne dane dodatkowe).
- **Odpowied� 201:** CustomerDto z pe�nymi danymi klienta.
- **B��dy:** 400 Bad Request (brak wymaganych p�l), 409 Conflict (docelowo ograniczenie duplikat�w).

### POST /api/holds
- **Opis:** tworzy tymczasowy hold na produkt.
- **Naglowki:** `X-Tenant-Id`.
- **Body:** `CreateHoldRequest`.
- **Odpowiedz 200:** `CreateHoldResponse` (guid holda + data wygasniecia).
- **Bledy:** `400` (walidacja), `401` (brak naglowka), `409` (zarezerwowane - TODO w przyszlosci).

Przyklad zadania:
```json
{
  "productId": "ad5c2fb5-5b8f-4cb5-bbda-38d6443c9f9e",
  "quantity": 2,
  "startDateUtc": "2025-09-30T08:00:00Z",
  "endDateUtc": "2025-10-02T18:00:00Z",
  "ttlMinutes": 20,
  "sessionId": "frontend-session-id"
}
```

### DELETE /api/holds/{id}
- **Opis:** usuwa hold (np. po rezygnacji klienta).
- **Statusy:** `204 No Content`, `404 Not Found`.

### POST /api/payments/quote
- **Opis:** oblicza kwot� ca�kowit� oraz depozyt dla przekazanych pozycji (mockowy kalkulator po stronie API).
- **Nag��wki:** `X-Tenant-Id`.
- **Body:** `PaymentQuoteRequest`.
- **Odpowied� 200:** `PaymentQuoteResponse` (zawiera `totalAmount`, `depositAmount`, `currency`, `rentalDays`).
- **B��dy:** `400 Bad Request` (brak pozycji, niepoprawne daty, brak produktu).

### POST /api/payments/intents
- **Opis:** tworzy mockow� intencj� p�atno�ci i od razu j� autoryzuje (status `Succeeded`).
- **Nag��wki:** `X-Tenant-Id`.
- **Body:** `CreatePaymentIntentRequest` (te same dane, co do wyliczenia kwoty).
- **Odpowied� 200:** `PaymentIntentDto` (kwota, depozyt, daty wa�no�ci, status).
- **B��dy:** `400 Bad Request` (niepoprawne dane, kwota = 0).

### GET /api/payments/intents/{id}
- **Opis:** zwraca szczeg�y mockowego `PaymentIntent` dla bie��cego tenanta.
- **Statusy:** `200 OK`, `404 Not Found` (brak lub wygas�a intencja).
### GET /api/my-rentals
- **Opis:** lista wynajm�w (dla UI klienta). Zwracane elementy zawieraj� m.in. `depositAmount` oraz `paymentStatus` do prezentacji p�atno�ci.
- **Parametry opcjonalne:** `status` (Pending/Confirmed/Completed/Cancelled), `from`, `to` (ISO 8601, UTC), `customerId` (Guid klienta).
- **Odpowiedz:** tablica `MyRentalDto` posortowana malejaco po `CreatedAtUtc`.

### POST /api/rentals
- **Opis:** tworzy nowy wynajem i weryfikuje mockow� p�atno�� (`paymentIntentId` z wcze�niejszego kroku).
- **Naglowki:** `X-Tenant-Id`.
- **Body:** `CreateRentalRequest` (co najmniej jedna pozycja, wymagany `paymentIntentId`).
- **Odpowiedz 200:** `RentalResponse` (status `Confirmed`, `totalAmount`, `depositAmount`, `paymentStatus`, opcjonalny `ContractUrl`).
- **Bledy:** `400 Bad Request` (walidacja dat, brak pozycji, brak/niepoprawny `paymentIntentId`), `409 Conflict` (TODO: kolizje dostepnosci), `404` (brak produktu/klienta).
### DELETE /api/rentals/{id}
- **Opis:** oznacza wynajem jako anulowany (status `Cancelled`).
- **Statusy:** `204 No Content`, `404 Not Found`.
### GET /api/customers/{id}
- **Opis:** zwraca dane konkretnego klienta w obr�bie bie��cego tenanta.
- **Statusy:** 200 OK, 404 Not Found.

### GET /api/customers/by-email
- **Opis:** wyszukuje klienta po adresie e-mail (case-insensitive) dla tenanta z nag��wka X-Tenant-Id.
- **Parametry:** email (query, wymagany).
  - Wykorzystywany przez klienta WASM do logowania/uzupe�niania danych (weryfikacja telefonu po stronie klienta).
- **Statusy:** 200 OK, 404 Not Found, 400 Bad Request (brak parametru).

### PUT /api/customers/{id}
- **Opis:** aktualizuje dane istniej�cego klienta.
- **Nag��wki:** X-Tenant-Id.
- **Body:** CreateCustomerRequest (pe�ne dane jak przy tworzeniu).
  - Zwraca zaktualizowany obiekt klienta wykorzystywany do od�wie�enia sesji w aplikacji klienckiej.
- **Statusy:** 200 OK, 400 Bad Request, 404 Not Found.
## Format bledow
API korzysta z `ProblemDetails`:
```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.1",
  "title": "Bad Request",
  "status": 400,
  "errors": {
    "items[0].quantity": ["Quantity must be at least 1"]
  },
  "traceId": "00-8a5f2f0c4f3f0844bb3dad..."
}
```

## Swagger
- Dev UI: `https://localhost:7142/swagger`.
- Dokument opisuje wymagany naglowek `X-Tenant-Id`; w UI nalezy dodac go recznie (Authorize -> ApiKey).

## MediaStorage API (⏸️ WYŁĄCZONY)

> **UWAGA:** Projekt `SportRental.MediaStorage` jest obecnie wyłączony.
> Pliki (zdjęcia produktów) są przechowywane bezpośrednio w **Azure Blob Storage**.
> Ta sekcja jest zachowana dla przyszłego użycia gdy zmiana hostingu (np. self-hosted bez Azure).

Magazyn plikow dziala pod domyslnym adresem `https://localhost:7002`.

| Metoda | Endpoint | Opis | Uwagi |
| --- | --- | --- | --- |
| `POST` | `/api/files` | Upload pliku | multipart form-data, pola `tenantId`, `path?`, `file`, naglowek `X-Api-Key` (jezeli skonfigurowano). |
| `GET` | `/api/files/{id}` | Metadane pliku | Zwraca `DownloadUrl`, hash SHA256. |
| `DELETE` | `/api/files/{id}` | Usuniecie pliku | Wymaga `X-Api-Key`. |
| `GET` | `/files/{**relativePath}` | Pobranie pliku | Zwraca strumien + MIME. |
| `HEAD` | `/files/{**relativePath}` | Sprawdzenie dostepnosci | `200`, `404` lub `400`. |

## Rate limiting i headery odpowiedzi
- Brak limitow (planowane w roadmapie: `AspNetCore.RateLimiting` na poziomie API publicznego).
- Odpowiedzi ustawiaja naglowek `Cache-Control: no-store` (domyslnie). CDN dla plikow obslugiwany jest poza API.

## Dalsze prace (skrot)
- Autoryzacja klientow (JWT) oraz refresh tokeny.
- Rozszerzenie filtrow i paginacji produktow.
- Statusy holdow i potwierdzenie kaucji.
- Webhooki powiadamiajace o zmianach statusu wynajmu.
- Integracja z produkcyjna bramka platnicza (Stripe/PayU) i webhookami.

Po szczegoly planu zajrzyj do `ROADMAP.md`.














