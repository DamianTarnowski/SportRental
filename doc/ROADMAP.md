# SportRental Roadmap

## Status projektu (grudzień 2025)
- **Panel administracyjny (SportRental.Admin):** ✅ Kompletne moduly CRUD, raporty, upload zdjęć z croppierem, konfiguracja firmy dla umów. ✅ **📱 Responsywne UI mobile-first** (dual UI strategy). ✅ **🌙 Dark mode**. Testy: 290+ scenariusze xUnit/bUnit.
- **API:** ✅ Hostowane w SportRental.Admin (nie osobny projekt). ✅ Stripe payment intents + webhooks, ✅ Email notifications z PDF, ✅ Multi-tenant.
- **Klient WASM (SportRental.Client):** ✅ Pełny flow zakupowy, ✅ Stripe Checkout Session, ✅ **📱 Mobile-first UI** z JS interop, ✅ Protected routes, ✅ Account management, ✅ Tailwind CSS, ✅ **🗺️ Mapa Leaflet**.
- **Media Storage:** ✅ Azure Blob Storage (produkcja) + automatyczne thumbnails (3 rozmiary), ✅ Image optimization (WebP), ✅ Lightbox UI.
- **Security:** ✅ Azure Key Vault dla WSZYSTKICH sekretów (connection strings, API keys), ✅ DefaultAzureCredential (az login → Managed Identity).
- **Infra:** .NET 10, PostgreSQL (Azure), Azure Blob Storage, Stripe sandbox, Onet SMTP, SMSAPI.pl, pełna dokumentacja.

## Co zostało zrealizowane (Q4 2025)
### ✅ Backend i API - COMPLETE
1. ✅ **JWT/Refresh Tokens** - pełna autoryzacja klientów z rotacją tokenów
2. ✅ **Stripe Integration** - Payment Intents + Checkout Sessions + Webhooks (sandbox)
3. ✅ **Email Notifications** - potwierdzenia wynajmu z załącznikami PDF (Onet SMTP)
4. ✅ **PDF Contracts** - automatyczne generowanie umów z danymi firmy (QuestPDF)
5. ✅ **Azure Key Vault** - wszystkie sekrety poza kodem (DefaultAzureCredential)
6. ✅ **Azure Blob Storage** - produkcyjny storage dla mediów z automatycznymi thumbnails
7. ✅ **Customer Endpoints** - `/api/auth/register`, `/api/auth/login`, `/api/customers`
8. ✅ **My Rentals** - historia wynajmów dla klienta z filtrowaniem

### ✅ Panel Administracyjny - ENHANCED
1. ✅ **Image Cropper** - Croppie.js z preview, rotate, zoom (max 8MB)
2. ✅ **Company Info Config** - panel konfiguracji danych firmy do umów (NIP, adres)
3. ✅ **Image Optimization** - automatyczne WebP thumbnails (small/medium/large)
4. ✅ **Lightbox** - pełnoekranowy podgląd zdjęć na kliknięcie
5. ✅ **📱 Responsywne UI** - dual UI strategy (mobile <768px / desktop)
6. ✅ **🌙 Dark Mode** - ThemeSwitcher z persystencją w localStorage
7. ✅ **Responsywne dialogi** - CustomerEdit, CustomerRentals, IssueEquipment, ReturnEquipment
8. ✅ **Mobile drawer** - nawigacja boczna na urządzeniach mobilnych

### ✅ Frontend Blazor WASM - MOBILE-FIRST
1. ✅ **📱 Mobile-First UI** - osobne widoki dla mobile/desktop
2. ✅ **JS Interop Mobile Detection** - wykrywanie rozmiaru ekranu (`mobile-detection.js`)
3. ✅ **Sticky UI Elements** - sticky headers, bottom summaries
4. ✅ **Kompaktowe karty** - siatka 2-kolumnowa na mobile
5. ✅ **Slidable filters** - panel filtrów na mobile
6. ✅ **🗺️ Mapa Leaflet** - interaktywna mapa wypożyczalni (`/map`)

### ✅ Lokalizacja i Mapa
1. ✅ **City/Voivodeship** - pola lokalizacji w Product i CompanyInfo
2. ✅ **API /api/tenants/locations** - endpoint dla mapy
3. ✅ **Filtrowanie po lokalizacji** - w UI produktów
4. ✅ **LeafletMap component** - do wyświetlania lokalizacji

## TODO - Pozostałe zadania Q4 2025
### Backend
- [ ] Walidacja dostępności w `POST /api/rentals` (konflikty, kolizje holdów)
- [ ] Rate limiting dla publicznego API
- [ ] Response caching
- [ ] Audit trail (tabela `AuditLogs`)

### Frontend
- [ ] Dynamiczny `TenantId` (subdomena lub runtime config)
- [ ] Monitoring błędów (Application Insights / Sentry)
- [ ] Multi-language support (i18n)

### Panel administracyjny
1. Dashboard z danymi tygodniowymi i planem oblozenia (wykorzystanie holdow/rentals).
2. Konfiguracja powiadomien SMS/Email per tenant (UI + serwisy).
3. Zadania cykliczne: czyszczenie holdow, archiwizacja dokumentow.

### Operacje i infrastruktura
1. Przygotowac pliki Docker dla wszystkich projektow (`docker-compose` do dev/test).
2. Zbudowac podstawowy pipeline CI (GitHub Actions): restore, build, test, raport coverage.
3. Przygotowac skrypt seeda danych demo (produkty, klienci, konta admin) do szybkich demo.
4. Migracja na finalne .NET 9 (gdy bedzie GA) i przegl?d bibliotek.

## Plan 2026 (wysoki poziom)
### Etap 1: MVP Plus (Q1 2026)
- Uruchomienie koszyka z platnosciami online.
- Integracja z kalendarzem Google/Microsoft (synchronizacja rezerwacji).
- Notyfikacje push/web (SignalR lub Azure Notification Hubs).

### Etap 2: MAUI i mobilnosc (Q2 2026)
- Projekt `SportRental.Maui` oparty o komponenty z `SportRental.Shared`.
- Funkcje mobilne: skanowanie QR, geolokalizacja punktow odbioru, offline cache.
- Publikacja w sklepach (Android/iOS) + CI mobilne.

### Etap 3: Skalowanie i analityka (Q3-Q4 2026)
- Wprowadzenie warstwy caching (Redis) oraz kolejek (Azure Service Bus/ RabbitMQ).
- Dashboard analityczny (Power BI / custom) + prognozowanie popytu.
- Wielojezycznosc i wielowalutowosc; biala etykieta dla partnerow.

## Kryteria sukcesu
| Obszar | Krotki termin (Q4 2025) | Sredni termin (2026) |
| --- | --- | --- |
| UX klienta | Pelny flow zamowienia, platnosc online, czytelne powiadomienia | Satysfakcja NPS > 45, retention mobilne > 60% |
| Technologia | 100% testow przechodzi, coverage > 80%, brak regresji | Monitorowana produkcja, alerty automatyczne, czas reakcji < 2s |
| Biznes | Minimum 3 tenanci produkcyjni | Ekspansja na kolejne rynki, integracje partnerskie |

## Zadania techniczne (backlog)
- Refaktoryzacja widokow MudBlazor do komponentow czastkowych.
- Indexy w bazie (produkty, holdy, rentals) + plan VACUUM/analiza.
- Observability: Serilog + Application Insights, logi strukturalne.
- Rate limiting oraz response caching w publicznym API.
- Audit trail dla operacji panelu (domyslnie logowanie do tabeli `AuditLogs`).

## Ryzyka i zaleznosci
- **.NET 9:** ✅ Używamy aktualnej wersji (plan upgrade do .NET 10 w Q1 2026).
- **Płatności:** ✅ Stripe sandbox wdrożony - przed produkcją wymaga aktywacji production keys.
- **Media hosting:** ✅ Azure Blob Storage wdrożony - CloudFlare CDN planowany dla Q1 2026.
- **Email:** ✅ Onet SMTP - wymaga monitorowania limitu wysyłki (może wymagać upgrade planu).
- **Azure Key Vault:** ⚠️ Krytyczna zależność - każdy developer musi mieć dostęp do Key Vault!

## Obsluga technicznego dlugu
- Aktualizacje pakietow kwartalnie (MudBlazor, EF Core, FluentAssertions, Azure SDK).
- Utrzymanie pokrycia testami powyzej 80% (krytyczne obszary: rezerwacje, platnosci, upload plikow).
- **Azure Key Vault:** Rotacja sekretów co kwartał (szczególnie JWT signing keys, API keys).
- Monitoring Key Vault access logs - wykrywanie unauthorized attempts.
- Code reviews z focus na security (never hardcode secrets!).

## Najblizsze kroki (Sprint następny)
1. **Docker + Docker Compose** - konteneryzacja wszystkich serwisów dla łatwego setupu
2. **CI/CD Pipeline** - GitHub Actions (build, test, deploy do Azure)
3. **Monitoring & Observability** - Application Insights integration
4. **Performance Testing** - load tests dla krytycznych endpointów
5. **CloudFlare CDN** - dla Azure Blob Storage (faster image delivery)


- Rozszerzy� samoobs�ug� klient�w o zarz�dzanie holdami i powiadomienia e-mail/SMS o zmianach status�w (po wdro�eniu pe�nej autoryzacji).

## Monitorowanie postepu
- Tablica kanban (Backlog, In progress, Code Review, Ready for QA, Done).
- Metryki tygodniowe: liczba PR, pokrycie testow, sredni czas builda, zgloszone bugi.
- Przeglad roadmapy raz w miesiacu (aktualizacja dokumentu + status).

Zwiekszanie wartosci dla klienta jest nadrzednym celem: kazda funkcja powinna wspierac proces wynajmu od wyszukania sprzetu, przez rezerwacje i platnosc, po zwrot. Dokument ma sluzyc jako odniesienie podczas planowania kolejnych sprintow.




