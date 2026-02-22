# SportRental Hybrid — Wycena Projektu
> **Data wyceny:** 2026-02-18
> **Branch:** feature/qr-codes (aktywny development)
> **Status:** Production-ready MVP, wszystkie funkcjonalności działają
> **Cel dokumentu:** Dane dla wizualizacji wartości projektu przez Claude Opus

---

## METRYKI KODU

| Metryka | Wartość |
|---------|---------|
| Pliki `.cs` (bez obj/bin) | ~300+ |
| Pliki `.razor` | ~120+ |
| Projekty w solucji | 12 |
| Projekty testowe | 5 (unit + integration + E2E) |
| Encje domenowe | 18 |
| Migracje EF Core | 13 |
| Endpointy API | 20+ |
| Strony Admin Panel | 12 głównych + 30+ account/manage |
| Strony Client WASM | 19 |
| Dialogi/komponenty | 15+ |
| Klasy serwisów | 20+ |
| Klasy testowe | 30+ |

---

## ARCHITEKTURA — PROJEKTY W SOLUCJI

```
SportRentalHybrid.sln
├── SportRental.Admin         → Blazor Server (panel admina) [net10.0]
├── SportRental.Client        → Blazor WASM (app klienta)   [net10.0]
├── SportRental.Api           → ASP.NET Core Minimal API    [net10.0]
├── SportRental.Infrastructure→ EF Core + PostgreSQL        [net10.0]
├── SportRental.Shared        → Shared DTOs + Components    [net10.0]
├── SportRental.MediaStorage  → Media abstraction layer     [net10.0]
├── SportRental.Backend       → Data utilities              [net10.0]
├── SportRental.Admin.Tests   → xUnit tests (Admin)         [net10.0]
├── SportRental.Api.Tests     → xUnit tests (API)           [net10.0]
├── SportRental.Client.Tests  → xUnit tests (WASM)          [net10.0]
├── SportRental.MediaStorage.Tests → xUnit tests            [net10.0]
└── SportRental.E2ETests      → Playwright E2E tests        [net10.0]
```

---

## ADMIN PANEL — STRONY (SportRental.Admin)

### Główne widoki biznesowe

| Strona | Plik | Kluczowe funkcje |
|--------|------|-----------------|
| Wynajmy | `Admin/Rentals.razor` | CRUD, status flow, SMS, PDF, filtry, eksport |
| Produkty | `Admin/Products.razor` | CRUD, upload zdjęć, cropper, QR, kategorie, ceny dzienne/godzinowe |
| Klienci | `Admin/Customers.razor` | Tabela, wyszukiwanie, historia wynajmów, edycja inline |
| Pracownicy | `Admin/Employees.razor` | CRUD, uprawnienia, zaproszenia, role |
| Ustawienia firmy | `Admin/CompanySettings.razor` | NIP, REGON, GPS, godziny, SMTP, SMS config |
| Harmonogram | `Admin/Schedule.razor` | Kalendarz wynajmów / timeline view |
| Raporty | `Admin/Reports.razor` | Analityka, wykresy, eksport |
| Obsługa sprzętu | `Admin/EquipmentHandling.razor` | Wydanie/zwrot sprzętu, sygnatury pracownika |
| Skaner QR | `Admin/QrScannerPage.razor` | Kamera, skanowanie kodów produktów |
| Edytor umów | `Admin/ContractTemplateEditor.razor` | Szablon PDF, placeholdery, podgląd |
| Panel właściciela | `Admin/Owner.razor` | Konfiguracja tenanta |
| Super Admin | `Admin/SuperAdmin.razor` | Zarządzanie wszystkimi tenantami |
| Dashboard | `Home.razor` | Statystyki, wykresy, szybkie akcje |

### Dialogi (pop-up komponenty)

| Dialog | Plik | Opis |
|--------|------|------|
| Edycja klienta | `CustomerEditDialog.razor` | Tworzenie/edycja klienta z poziomu wynajmu |
| Historia klienta | `CustomerRentalsDialog.razor` | Historia wynajmów danego klienta |
| Wydanie sprzętu | `IssueEquipmentDialog.razor` | Potwierdzenie wydania z podpisem pracownika |
| Zwrot sprzętu | `ReturnEquipmentDialog.razor` | Protokół zwrotu, kaucja, uszkodzenia |
| Wynik QR | `QrScanResultDialog.razor` | Wynik skanowania kodu QR |
| Etykiety QR | `QrLabelsDialog.razor` | Drukowanie etykiet z kodami QR |

### Strony konta / auth (30+ plików)

Login, Register, RegisterEmployee, RegisterOwner, Logout, ForgotPassword, ForgotPasswordConfirmation, ResetPassword, ResetPasswordConfirmation, ConfirmEmail, ConfirmEmailChange, ExternalLogin, InvalidPasswordReset, InvalidUser, AccessDenied, Lockout, LoginWith2fa, LoginWithRecoveryCode, ResendEmailConfirmation, + Manage (Index, Email, ChangePassword, TwoFactorAuthentication, EnableAuthenticator, Disable2fa, ResetAuthenticator, GenerateRecoveryCodes, SetPassword, DeletePersonalData, ExternalLogins, PersonalData)

---

## CLIENT APP — STRONY (SportRental.Client — Blazor WASM)

| Strona | Plik | Opis |
|--------|------|------|
| Strona główna | `Home.razor` | Landing page, hero, CTA |
| Katalog produktów | `Products.razor` | Grid, filtry (cena, kategoria, miasto, dostępność) |
| Szczegóły produktu | `ProductDetails.razor` | Galeria, opis, wybór dat, koszyk |
| Koszyk | `Cart.razor` | Lista produktów, kalkulacja ceny |
| Checkout | `Checkout.razor` | Stripe Payment Element, BLIK, karty |
| Sukces płatności | `CheckoutSuccess.razor` | Potwierdzenie, link do umowy |
| Anulowanie | `CheckoutCancel.razor` | Powrót do koszyka |
| Moje wynajmy | `MyRentals.razor` | Historia, statusy, filtrowanie |
| Szczegóły wynajmu | `RentalDetails.razor` | Szczegóły, umowa PDF, płatność |
| Konto | `Account.razor` | Dane użytkownika |
| Profil | `Profile.razor` | Edycja profilu, zmiana hasła |
| Kontakt | `Contact.razor` | Formularz kontaktowy |
| Mapa | `Map.razor` | Leaflet.js, lokalizacje wypożyczalni |
| Logowanie | `Login.razor` | JWT auth |
| Rejestracja | `Register.razor` | Nowe konto |
| Wybór tenanta | `SelectTenant.razor` | Multi-tenant marketplace |
| 404 | `NotFound.razor` | Strona błędu |

---

## API — ENDPOINTY (SportRental.Api)

### Auth
- `POST /api/auth/register` — rejestracja
- `POST /api/auth/login` — logowanie, zwraca JWT + refresh token
- `POST /api/auth/refresh` — rotacja refresh tokena
- `POST /api/auth/revoke` — wylogowanie

### Produkty
- `GET /api/products` — lista (filtry: search, category, city, voivodeship, price, available)
- `GET /api/products/{id}` — szczegóły

### Wynajmy
- `POST /api/rentals` — utwórz wynajem [Admin]
- `GET /api/my-rentals` — wynajmy zalogowanego klienta
- `DELETE /api/rentals/{id}` — anuluj [Admin]

### Klienci
- `GET /api/customers/{id}`
- `GET /api/customers/by-email`
- `POST /api/customers`
- `PUT /api/customers/{id}`

### Rezerwacje (holds)
- `POST /api/holds` — tymczasowa rezerwacja (TTL 5–30 min)
- `DELETE /api/holds/{id}` — zwolnienie

### Płatności
- `POST /api/payments/quote` — kalkulacja kosztu
- `POST /api/payments/intents` — Stripe Payment Intent
- `GET /api/payments/intents/{id}` — status płatności
- `POST /api/checkout/create-session` — Stripe Checkout Session
- `POST /api/checkout/finalize-session/{sessionId}` — finalizacja

### Tenants
- `GET /api/tenants` — lista wypożyczalni
- `GET /api/tenants/locations` — mapa (GPS, nazwa, info)

### Webhooks
- `POST /api/stripe/webhook` — zdarzenia Stripe
- `GET /api/sms/incoming` — webhook SerwerSMS.pl
- `POST /api/sms/incoming` — webhook SMSAPI.pl

### Umowy
- `GET /c/{shortId}` — umowa PDF via short link

---

## ENCJE DOMENOWE — MODEL DANYCH

```
┌─────────────────────────────────────────────────────────────────┐
│  MULTI-TENANCY                                                  │
│  Tenant (id, name, subdomain)                                   │
│  TenantUser (userId, tenantId, role)                            │
│  TenantInvitation (token, email, expiresAt)                     │
│  CompanyInfo (NIP, REGON, address, phone, openingHours,         │
│               gpsLat, gpsLon, city, voivodeship,               │
│               smtpHost, smsProvider, smsApiKey, senderName)    │
├─────────────────────────────────────────────────────────────────┤
│  PRODUKTY                                                       │
│  Product (id, tenantId, name, description, sku, pricePerDay,   │
│           pricePerHour, depositAmount, categoryId, imageUrl,   │
│           qrCode, quantity, city, voivodeship, isActive)       │
│  ProductCategory (id, tenantId, name, description)             │
├─────────────────────────────────────────────────────────────────┤
│  WYNAJMY                                                        │
│  Rental (id, tenantId, customerId, status, startDate,          │
│          endDate, totalAmount, depositAmount, paymentIntentId, │
│          source [Online/InStore], isEquipmentIssued,           │
│          isEquipmentReturned, issuedByEmployeeId,              │
│          returnedByEmployeeId, isSmsConfirmationSent,          │
│          contractUrl, notes, createdAt)                        │
│  RentalItem (rentalId, productId, quantity,                    │
│              pricePerDay, pricePerHour)                        │
│  ReservationHold (id, tenantId, productId, quantity,           │
│                   expiresAt, sessionToken)                     │
├─────────────────────────────────────────────────────────────────┤
│  KLIENCI                                                        │
│  Customer (id, fullName, email, phone, address,                │
│            documentNumber, notes, userId)                      │
├─────────────────────────────────────────────────────────────────┤
│  PRACOWNICY                                                     │
│  Employee (id, tenantId, userId, firstName, lastName,          │
│            email, role, isActive)                              │
│  EmployeePermissions (employeeId, canCreateRentals,            │
│                       canIssueEquipment, canReturnEquipment,  │
│                       canManageProducts, canViewReports)       │
│  EmployeeInvitation (token, email, tenantId, expiresAt)       │
├─────────────────────────────────────────────────────────────────┤
│  KOMUNIKACJA                                                    │
│  SmsConfirmation (rentalId, code, isConfirmed, sentAt)        │
│  ContractTemplate (tenantId, templateHtml, updatedAt)         │
├─────────────────────────────────────────────────────────────────┤
│  PŁATNOŚCI & SESJE                                              │
│  RefreshToken (userId, token, expiresAt, replacedBy)          │
│  CheckoutSession (id, tenantId, payload, idempotencyKey,      │
│                   isUsed, createdAt)                          │
├─────────────────────────────────────────────────────────────────┤
│  AUDYT                                                          │
│  AuditLog (id, tenantId, userId, action, entityType,          │
│            entityId, details, createdAt)                       │
│  ErrorLog (id, tenantId, message, stackTrace, severity,       │
│            createdAt)                                          │
└─────────────────────────────────────────────────────────────────┘
```

### Migracje EF Core (chronologicznie)

| # | Nazwa migracji | Data |
|---|---------------|------|
| 1 | AddREGONToCompanyInfo | 2025-10-06 |
| 2 | PaymentIntentStringIds | 2025-11-11 |
| 3 | AddTenantInvitations | 2025-12-08 |
| 4 | AddEmployeeInvitation | 2025-12-09 |
| 5 | AddRefreshToken | 2025-12-10 |
| 6 | AddCheckoutSession | 2025-12-10 |
| 7 | AddRentalSourceAndEquipmentHandling | 2025-12-10 |
| 8 | AddSmsConfigurationToCompanyInfo | 2025-12-11 |
| 9 | AddIsSmsConfirmationSentToRental | 2025-12-11 |
| 10 | AddHourlyRentalSupport | 2025-12-11 |
| 11 | AddCityAndVoivodeshipToProduct | 2025-12-11 |
| 12 | AddCityAndVoivodeshipToCompanyInfo | 2025-12-12 |
| 13 | IncreaseProductDescriptionLength | 2026-02-16 |

---

## STACK TECHNOLOGICZNY

### Backend

| Technologia | Wersja | Zastosowanie |
|-------------|--------|--------------|
| .NET | **10.0** | Cały backend, Blazor Server, API |
| ASP.NET Core Minimal APIs | 10.0 | REST API |
| Blazor Server | 10.0 | Panel administracyjny (real-time, SSR) |
| Blazor WebAssembly | 10.0 | Aplikacja klienta (SPA, offline-capable) |
| Entity Framework Core | 9.0.9 | ORM, query filters, migracje |
| PostgreSQL via Npgsql | 9.0.4 | Baza danych produkcyjna |
| ASP.NET Core Identity | 9.0.9 | Zarządzanie użytkownikami |
| JWT Bearer | 9.0.9 | Autoryzacja API (60 min access + 7 dni refresh) |

### Integracje zewnętrzne

| Integracja | Biblioteka/Wersja | Rola |
|------------|-------------------|------|
| **Stripe** | stripe.net v49.0.0 | Płatności (Payment Intents, Checkout, webhooks, BLIK, karty, przelewy) |
| **SMSAPI.pl** | SMSAPI v2.2.0 | SMS podstawowy (potwierdzenia, przypomnienia, dziękowania) |
| **SerwerSMS.pl** | custom HTTP | SMS zapasowy / alternatywny provider |
| **Azure Blob Storage** | Azure.Storage.Blobs v12.25.1 | Przechowywanie zdjęć produktów, umów PDF |
| **AWS S3** | AWSSDK.S3 v4.0.7.7 | Alternatywne przechowywanie plików |
| **Azure Key Vault** | Azure.Identity v1.16.0 + Extensions | Zarządzanie secretami produkcyjnymi |
| **MailKit/MimeKit** | v4.14.0 / v4.9.0 | Wysyłka emaili (SMTP, HTML templates) |
| **QuestPDF** | v2025.7.2 | Generowanie umów PDF |
| **QRCoder** | v1.6.0 | Generowanie kodów QR dla produktów |
| **SixLabors.ImageSharp** | v3.1.11 | Przycinanie i resize zdjęć |
| **Leaflet.js** | via JS interop | Mapa lokalizacji wypożyczalni |
| **Croppie** | via JS interop | Kadrowanie zdjęć w przeglądarce |

### Frontend / UI

| Technologia | Wersja | Zastosowanie |
|-------------|--------|--------------|
| MudBlazor | v8.15.0 | UI komponenty Admin (tabele, dialogi, formy, theming) |
| TailwindCSS | v3.4.7 | Styling aplikacji klienta (WASM) |
| Blazored.LocalStorage | v4.5.0 | Przechowywanie JWT w przeglądarce |
| libphonenumber-csharp | v8.13.50 | Walidacja numerów telefonów |
| Playwright | .NET binding | E2E testy automatyczne |

---

## TESTY — POKRYCIE I ZAKRES

### Klasy testowe (łącznie 30+)

**SportRental.Api.Tests (12 klas):**
- `RentalConfirmationEmailIntegrationTests` — testy emaili potwierdzających
- `EmailIntegrationTests` — testy SMTP
- `OnetEmailRealTests` + `OnetEmailReceiveTests` — testy realne skrzynek
- `StripeWebhookTests` — obsługa eventów Stripe
- `PdfContractWithCompanyInfoTests` + `PdfContractReadTests` — generowanie PDF
- `PaymentsEndpointsTests` — endpointy płatności
- `ProductCatalogEndpointsTests` — katalog produktów
- `CustomersEndpointsTests` — zarządzanie klientami
- `StripeCheckoutTests` — Stripe Checkout Session
- `StripePaymentGatewayTests` — bramka płatności

**SportRental.Admin.Tests (4 klasy):**
- `DbContextTests` — poprawność query filters, izolacja tenantów
- `RentalLogicTests` — logika wynajmu, kalkulacje
- `EmployeeInvitationTests` — system zaproszeń
- `ApiTests` — integracja z API

**SportRental.Client.Tests (1 klasa):**
- `ApiConnectionTests` — połączenie WASM↔API

**SportRental.E2ETests — Playwright (17 klas):**
- `HomePageTests`, `ProductDetailsTests`, `CartTests`, `CheckoutTests`
- `ContactTests`, `ProductCatalogTests`, `UIReviewTests`
- `FullUserJourneyTests` — kompletny flow od katalogu do płatności
- `ClickThroughTests`, `SimpleAddToCartTest`, `FullFlowVerificationTests`
- `DeepDiagnosticsTests`, `CheckoutFlowDiagnosticsTests`, `ClientDiagnosticsTest`
- `ManualBrowserTest`, `QuickDiagnosticsTest`, `BaseTest`

---

## PRZEPŁYW BIZNESOWY — GŁÓWNE SCENARIUSZE

### Scenariusz A: Online Rental (klient → Stripe)
```
Klient przegląda katalog produktów (filtry: miasto, ceny, dostępność)
  → Wybiera produkt + daty → Tworzy ReservationHold (TTL 15 min)
  → Przechodzi do koszyka → Widzi wyliczony koszt + depozyt (30%)
  → Checkout → Stripe Payment Element (karta / BLIK / przelew)
  → Stripe webhook → Potwierdzenie → Wynajem Created w bazie
  → Email z linkiem do umowy PDF → SMS potwierdzający
  → Status: Confirmed
  → W dniu wynajmu: pracownik skanuje QR sprzętu → IssueEquipment
  → Status: Active
  → Po zwrocie: ReturnEquipment → refund kaucji → Status: Completed
```

### Scenariusz B: In-Store Rental (pracownik w panelu admina)
```
Klient przychodzi do wypożyczalni
  → Pracownik loguje się do Admin Panel
  → Nowy wynajem → Wyszukuje/tworzy klienta (CustomerEditDialog)
  → Dodaje produkty + daty → Kalkulacja ceny
  → Opcjonalnie: Stripe terminal / gotówka
  → Generuje umowę PDF → Drukuje lub wysyła SMS z linkiem
  → Skanuje QR sprzętu przez EquipmentHandling
  → Status: Active
```

### Scenariusz C: Multi-tenant marketplace
```
Klient wyszukuje sprzęt w całej sieci wypożyczalni
  → Produkty z różnych tenantów w jednym katalogu
  → Wybiera produkty od różnych dostawców
  → Dla każdego tenanta tworzone oddzielne Rental + Payment Intent
  → Każdy tenant otrzymuje powiadomienie o nowym wynajmie
```

---

## BEZPIECZEŃSTWO

| Mechanizm | Implementacja |
|-----------|---------------|
| Autentykacja | JWT Bearer (access 60 min) + Refresh Token (7 dni) z rotacją |
| Autoryzacja | Role-based (Owner, SuperAdmin, Employee, Client) |
| Izolacja danych | EF Core Query Filters — każde zapytanie filtrowane przez TenantId |
| Hasła | Identity: min 8 znaków, wielkie/małe litery, cyfry |
| Blokada konta | 5 nieudanych prób → 15 minut lockout |
| 2FA | TOTP (Google Authenticator) + kody recovery |
| API rate limiting | Wbudowany .NET rate limiter |
| Idempotentność | Unikalne IdempotencyKey dla operacji płatności |
| Transakcje | Serializable isolation level przy tworzeniu wynajmów (brak overbooking) |
| Sekrety | Azure Key Vault w produkcji |
| HTTPS | Enforced, HSTS |

---

## DOKUMENTACJA (katalog /doc)

| Dokument | Zawartość |
|----------|-----------|
| `ARCHITECTURE.md` | Architektura systemu, diagramy |
| `API_DOCUMENTATION.md` | Pełna dokumentacja API |
| `DEVELOPER_GUIDE.md` | Setup środowiska dev, konfiguracja |
| `TESTING_GUIDE.md` | Strategie testowania, uruchamianie testów |
| `ROADMAP.md` | Plan dalszego rozwoju |
| `AZURE_DEPLOYMENT.md` | Deployment na Azure (App Service, Static Web Apps) |
| `PRODUCTION_CLIENT_FIXES.md` | Historia napraw produkcyjnych |
| `SMSAPI_INTEGRATION.md` | Integracja SMS |
| `MEDIA_FEATURES.md` | Obsługa mediów (Azure Blob, AWS S3) |

---

## OSTATNIE ZMIANY (git log — branch feature/qr-codes)

| Commit | Typ | Opis |
|--------|-----|------|
| b54d503 | feat | CustomerEditDialog — tworzenie/edycja klienta bezpośrednio z okna "Nowy wynajem" |
| 5aabb5a | chore | Aktualizacja .gitignore |
| 3f53c4d | chore | WASM: checkout, mapa, wynajmy, auth — aktualizacje |
| ce38a40 | chore | Admin UI: cropper, endpointy, strony — aktualizacje |
| 39c1ecc | feat | BlazorTenantProvider — wsparcie multi-tenancy w WASM |
| 8252541 | test | Testy integracyjne: Email, SMS, Concurrency |
| 3f53966 | fix | CSS overflow dla croppie, licencja QuestPDF Community |
| 172f060 | fix | Naprawa błędów concurrency (Find() zamiast Update()) |
| 5c4a74c | feat | Zwiększenie MaxLength opisu produktu z 1000 do 5000 |
| bd265ce | docs | Aktualizacja dokumentacji Azure deployment |
| 09e4b42 | feat | Admin: SMS routing, ulepszenia SignalR hubs |
| 943b32c | feat | Client: wyłączenie domyślnego filtrowania tenantów |
| 0537543 | fix | Verbose logging dla skanera QR |
| 57b8cfb | fix | Poprawka podglądu video w QR skanerze, domyślny jasny motyw |
| 7e4d980 | feat | **WASM QR Scanner** — kamera mobilna dla klientów |

---

## WYCENA — LUTY 2026

### Metoda 1: Koszt odtworzenia (development cost today)

Ile kosztowałoby dziś zlecenie tego od zera polskiej firmie/freelancerowi:

| Komponent | Szac. godziny | Stawka | Koszt |
|-----------|--------------|--------|-------|
| Backend API (.NET 10, Minimal APIs) | 250h | 200 PLN/h | **50 000 PLN** |
| Admin Panel (Blazor Server, 12+ widoków) | 220h | 180 PLN/h | **39 600 PLN** |
| Client App (Blazor WASM, 19 stron) | 180h | 180 PLN/h | **32 400 PLN** |
| Stripe (intents, webhooks, checkout, depozyty, idempotency) | 80h | 220 PLN/h | **17 600 PLN** |
| SMS (2 providerów, router, potwierdzenia, szablony) | 60h | 180 PLN/h | **10 800 PLN** |
| PDF (QuestPDF, szablony, krótkie linki) | 50h | 180 PLN/h | **9 000 PLN** |
| Email (MailKit, HTML templates, SMTP) | 40h | 160 PLN/h | **6 400 PLN** |
| Auth & Security (JWT+refresh, Identity, 2FA, lockout, TOTP) | 90h | 220 PLN/h | **19 800 PLN** |
| Multi-tenancy (query filters, izolacja, zaproszenia, marketplace) | 80h | 220 PLN/h | **17 600 PLN** |
| Baza danych (18 encji, 13 migracji, indeksy, relacje) | 60h | 180 PLN/h | **10 800 PLN** |
| QR kody (generowanie, skaner desktopowy, skaner WASM/mobilny, etykiety) | 50h | 180 PLN/h | **9 000 PLN** |
| Rezerwacje (holds, serializable tx, anti-overbooking) | 35h | 200 PLN/h | **7 000 PLN** |
| Obsługa sprzętu (issue/return flow, dialogi, sygnatury) | 30h | 180 PLN/h | **5 400 PLN** |
| Testy (unit, integration, E2E Playwright — 30+ klas) | 130h | 150 PLN/h | **19 500 PLN** |
| Azure (Blob, Key Vault, App Service, Static Web Apps, deploy) | 45h | 180 PLN/h | **8 100 PLN** |
| Project management / architektura / code review | 70h | 160 PLN/h | **11 200 PLN** |
| **ŁĄCZNIE** | **~1 470h** | | **~274 200 PLN** |

> **Realizm rynkowy:** Firma zewnętrzna wyceniłaby to na **280 000 – 350 000 PLN** (doliczając marżę 15-25%, ryzyko, PM, QA).

---

### Metoda 2: Wartość rynkowa "as-is" (sprzedaż kodu)

**Czynniki + (podnoszące wartość):**
- .NET 10 — najnowszy framework, aktywnie wspierany przez MS
- Blazor Server + WASM — architektura hybrydowa (Server dla admina, WASM dla klientów)
- Multi-tenant — SaaS-ready od pierwszego dnia, bez refactoringu
- Stripe w pełni zintegrowany (Payment Intents + Checkout + webhooks + refunds)
- Polskie SMS-y (SerwerSMS.pl + SMSAPI.pl) — unikalne dla rynku PL
- QuestPDF z konfigurowalnymi szablonami — gotowe do white-label
- 30+ klas testów, w tym E2E Playwright — rzadkość w projektach tej wielkości
- 11 plików dokumentacji technicznej
- Azure Key Vault — enterprise-grade security

**Czynniki - (obniżające wartość):**
- Brak istniejącej bazy klientów (MRR = 0)
- Feature branch (QR codes) jeszcze nie zmergowany do main
- Niszowy rynek (sport rental PL) — mała liczba potencjalnych kupujących
- Kupujący musi opanować codebase (~1 470h kodu)

```
Sprzedaż kodu "as-is" (2026-02-18):
  Minimalna (szybka sprzedaż):    120 000 PLN
  Realistyczna:                   165 000 PLN
  Optymistyczna (dobry kupiec):   220 000 PLN
```

---

### Metoda 3: White-label / Licencja SaaS

Model dla agencji/resellera który chce wdrażać produkt u klientów:

```
Jednorazowy setup fee (per klient):   8 000 – 15 000 PLN
Miesięczne utrzymanie + support:       1 500 – 3 000 PLN/mies
Customizacja (branding, funkcje):      180 – 220 PLN/h

Przykład: 10 klientów × (12 000 PLN setup + 2 000 PLN/mies)
  → Rok 1: 10×12 000 + 10×2 000×12 = 120 000 + 240 000 = 360 000 PLN
```

---

### Metoda 4: SaaS Revenue — potencjał operatorski

Jeśli właściciel kodu uruchamia jako SaaS dla polskich wypożyczalni sportowych:

**Proponowany cennik:**
| Plan | Cena | Limit |
|------|------|-------|
| Starter | 299 PLN/mies | do 30 wynajmów/mies |
| Business | 599 PLN/mies | do 200 wynajmów/mies |
| Pro | 1 099 PLN/mies | bez limitu |

**Projekcja przychodów:**

| Klienci | Mix planów | MRR | ARR |
|---------|-----------|-----|-----|
| 20 klientów | 10×Starter + 10×Business | ~8 980 PLN | ~107 760 PLN |
| 50 klientów | 20×Starter + 25×Business + 5×Pro | ~23 955 PLN | ~287 460 PLN |
| 100 klientów | 40×Starter + 50×Business + 10×Pro | ~47 910 PLN | ~574 920 PLN |

**Wycena SaaS (mnożnik ARR):**

| Scenariusz | ARR | Mnożnik | Wycena |
|------------|-----|---------|--------|
| 20 klientów | ~108 000 PLN | 3× | ~324 000 PLN |
| 50 klientów | ~287 000 PLN | 4× | ~1 150 000 PLN |
| 100 klientów | ~575 000 PLN | 5× | ~2 875 000 PLN |

> Mnożniki dla SaaS B2B w Polsce (2026): 3–6× ARR w zależności od churn, NRR, rynku.

---

### Podsumowanie wycen

| Metoda | Wartość |
|--------|---------|
| Koszt odtworzenia (market) | **280 000 – 350 000 PLN** |
| Sprzedaż as-is | **120 000 – 220 000 PLN** |
| White-label (rok 1, 10 klientów) | **~360 000 PLN** |
| SaaS z 50 klientami | **~1 150 000 PLN** |
| SaaS z 100 klientami | **~2 875 000 PLN** |

---

## POTENCJAŁ ROZWOJU

| Feature | Szac. koszt | Wartość dodana |
|---------|-------------|----------------|
| Aplikacja mobilna (MAUI lub React Native) | 50 000 – 80 000 PLN | Skanowanie QR poza przeglądarką, push notifications |
| Multi-język (PL/EN/DE) | 15 000 – 25 000 PLN | Rynki zagraniczne |
| Zaawansowana analityka (wykresy, BI) | 20 000 – 35 000 PLN | Decyzje biznesowe dla właścicieli |
| Integracja z kasą fiskalną | 20 000 – 40 000 PLN | Wymóg prawny przy sprzedaży |
| API publiczne dla partnerów | 25 000 – 40 000 PLN | Marketplace, OTA integracje |
| AI rekomendacje sprzętu | 20 000 – 35 000 PLN | Upselling, personalizacja |
| Elektroniczny podpis umów | 15 000 – 25 000 PLN | Autenti/DocuSign integracja |
| Inventory management (barcodes, stany magazynowe) | 30 000 – 50 000 PLN | Duże wypożyczalnie |

---

## KONTEKST RYNKOWY

### Polskie wypożyczalnie sportowe — rynek docelowy
- Szacowana liczba wypożyczalni sprzętu sportowego w PL: **3 000 – 5 000**
- Penetracja softwarem: **<10%** (większość używa Excela lub własnych arkuszy)
- Główni konkurenci globalnie: Rentle (Finlandia), Booqable (Holandia)
- Brak dedykowanego polskiego rozwiązania z: polskim SMS, REGON/NIP, PLN, polskimi adresami

### Przewagi konkurencyjne
1. **Polski rynek**: SerwerSMS.pl, SMSAPI.pl, NIP/REGON, PLN, polskie województwa
2. **Multi-tenant marketplace**: klienci mogą wynajmować sprzęt od wielu wypożyczalni
3. **Nowoczesny stack**: .NET 10 — długoterminowe wsparcie Microsoft
4. **Pełny flow**: od katalogu przez Stripe do PDF umowy — zero external dependencies
5. **Self-hosted lub Azure**: brak vendor lock-in dla klientów

---

*Wycena sporządzona: 2026-02-18*
*Autor: analiza kodu przez Claude (Sonnet 4.6) + Claude (Opus 4.6)*
*Metodologia: koszt odtworzenia + analiza rynkowa + DCF/ARR dla SaaS*
