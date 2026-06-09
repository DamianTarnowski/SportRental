# Roadmapa parytetu z Bookero (RentSpot, Q2–Q4 2026)

> Plan implementacji funkcji które Bookero ma, a nas brakuje — w kolejności priorytetu biznesowego (przed launchem partnerskim, post-launch, skalowanie). Każda faza opisana z entities/migracji/UI/services/testów na poziomie pozwalającym puścić sprint.
>
> Stan: 2026-05-07. Bazuje na [docs/competitor_analysis_bookero.md](competitor_analysis_bookero.md) i kodzie repo `main` @ commit `a6e1b77`.

## Założenia ogólne

- **Cel:** zamknąć krytyczne luki funkcyjne Bookero **przed** wysłaniem oferty do 1330 partnerów (target: lipiec 2026)
- **Priorytetyzacja:** każda faza ma kolor:
  - 🔴 **blocker** dla launchu partnerskiego — bez tego wypożyczalnia 30+ wynajmów/mc nas odrzuci
  - 🟡 **wartość** — wzmacnia konkurencyjność, ale nie blokuje
  - 🟢 **scaling** — dla większych partnerów, post-launch
- **Każda faza:** entities + migracje EF + service layer + UI Admin/Client + testy unit/integration + smoke prod
- **Iteracja:** każda faza = 1 commit, deploy do `srental2`, smoke test, dopiero kolejna
- **Testy:** preferujemy live PostgreSQL test DB (nie Testcontainers — memory `feedback_no_docker`); LLM-y bez mocków (memory `feedback_no_mock_llm`)
- **Sekrety:** wszystkie tokeny (Google OAuth, MS Graph, Fakturownia API key) → Azure Key Vault, repo public

---

## ŁĄCZNIK: poprzedni stan (Q2 2026, w toku)

| Task | Status |
|---|---|
| Rebrand SportRental → RentSpot (UI/theme/PDF/favicon) | ✅ commit `a6e1b77`, prod |
| Subdomena `app.rentspot.eu` | ⏳ task #68 — DNS API Hostinger 403 (świeża domena), retry za godzinę |
| Maile transakcyjne z `@rentspot.eu` | ⏸️ czeka na DNS + skrzynkę noreply@ w hPanel |

---

## FAZA 8 — krytyczne luki Bookero (Q2 2026, ~3 tygodnie)

🔴 **Blocker dla launchu**. Bez tych trzech funkcji partner zawodowy z 30+ wynajmami/mc nas odrzuci po pierwszym demo.

### 8a. Godziny pracy (BusinessHours per-tenant)

**Co robimy:** każdy partner ustawia godziny otwarcia (per dzień tygodnia) i listę dni wolnych. Klient nie może rezerwować poza godzinami; admin może zrobić "ręczny override" (przez `IssueEquipmentDialog`).

**Entity:**
```csharp
public class BusinessHoursSchedule {
  Guid Id; Guid TenantId;
  // Per-day: 0=Mon..6=Sun
  public ICollection<BusinessHoursDay> Days; // {DayOfWeek, OpenFrom, OpenTo, IsClosed}
}
public class BusinessHoursException {
  Guid Id; Guid TenantId;
  DateOnly Date; bool IsClosed;
  TimeOnly? CustomOpen; TimeOnly? CustomClose;
  string? Reason; // "Boże Narodzenie", "Inwentaryzacja"
}
```

**Migracja:** `20260520_AddBusinessHours` (~30 min). Default seed dla istniejących tenantów: 8:00–20:00 codziennie.

**Service:** `IBusinessHoursService`
- `Task<bool> IsOpenAt(Guid tenantId, DateTime utc)` — uwzględnia tz `Europe/Warsaw`
- `Task<List<TimeWindow>> GetAvailableSlots(Guid tenantId, DateOnly date)` — dla klient-side date picker

**Walidacja:**
- W `CheckoutFinalizationService.FinalizeAsync`: jeśli `start/end` poza godzinami → reject
- W `Rentals.razor` admin dialog: warning + checkbox "wymuś poza godzinami" (audit log)

**UI:**
- `/admin/company-settings` → tab "Godziny pracy" (grid 7 dni × picker from/to + checkbox "zamknięte")
- Sub-section "Dni wolne" (lista dat z reasonem, add/remove)
- Client `Cart.razor` checkout: date/time picker disabled poza godzinami + tooltip "Wypożyczalnia jest zamknięta w tym czasie"

**Testy:**
- Unit: `BusinessHoursService.IsOpenAt` — 20 case'ów (przed/w/po godzinach, dni wolne, ekspceptions, midnight crossing)
- Integration: POST `/api/checkout/create-session` z time poza godzinami → 422

**Estimate:** 3 dni roboty + 1 dzień testów + 0.5 dnia smoke prod.

---

### 8b. Reguły cen sezonowych (PriceRule per-product)

**Co robimy:** Każdy produkt ma bazową cenę/dzień. Plus opcjonalnie N reguł cenowych z date-range: "1 lipca – 31 sierpnia: ×1.5", "Boże Narodzenie: +50 PLN flat". Resolver wybiera najwyższy priorytet aktywny w czasie wynajmu.

**Entity:**
```csharp
public class PriceRule {
  Guid Id; Guid TenantId; Guid ProductId;
  string Name; // "Wysoki sezon", "Ferie zimowe"
  DateOnly FromDate; DateOnly ToDate;
  PriceRuleType Type; // Multiplier | FixedAdd | FixedReplace
  decimal Value; // 1.5 albo 50.00
  int Priority; // wyższy = wygrywa
  bool IsActive;
}
public enum PriceRuleType { Multiplier, FixedAdd, FixedReplace }
```

**Migracja:** `20260523_AddPriceRules`

**Service:** `IPriceCalculator`
- `decimal CalculateDailyPrice(Product p, DateOnly day)` — pobiera bazową, aplikuje highest-priority active rule
- `decimal CalculateRentalTotal(Rental r, IEnumerable<RentalItem>)` — iteruje dni × items, sumuje

**Walidacja:**
- W `CartService.AddItem` i `CheckoutFinalizationService` używamy zawsze `IPriceCalculator`, nie raw `Product.DailyPrice`
- Wyszczególnienie cen per-dzień widoczne w PDF umowy (nowa sekcja "Rozliczenie sezonowe")

**UI:**
- `/admin/products/{id}` → nowy tab "Ceny sezonowe": tabela rules + add/edit/delete
- Wizualizacja: kalendarz miesięczny z kolorami per rule (heat-map)
- Client `ProductDetails.razor`: pokazujemy ceny dla wybranego okresu (od-do), nie tylko bazowe

**Testy:**
- Unit: `PriceCalculator` — 15 case'ów (overlap rules, priorytet, brak rule = bazowa, multiplier × add, year-end span)
- Integration: pełen checkout w okresie z rule → suma poprawna w Stripe sessions metadata

**Estimate:** 4 dni + 1 dzień testów + 0.5 smoke.

---

### 8c. Faktury VAT (prosty PDF + opcjonalnie Fakturownia)

**Co robimy:** Etap 1 (must): generujemy PDF VAT z numerem `FV/{YYYY}/{NNNN}` per-tenant per-rok, linkujemy z rentalem. Etap 2 (nice-to-have): Fakturownia API jeśli partner ma konto.

**Entity:**
```csharp
public class Invoice {
  Guid Id; Guid TenantId; Guid RentalId; Guid CustomerId;
  string Number; // "FV/2026/0001"
  DateTime IssuedAtUtc;
  DateTime DueAtUtc; // domyślnie +14 dni
  decimal NetAmount; decimal VatAmount; decimal GrossAmount;
  string VatRate; // "23%", "8%", "zw."
  InvoiceStatus Status; // Draft, Issued, Paid, Cancelled
  string? FakturowniaExternalId; // jeśli używamy ich API
  string PdfUrl; // blob storage path
  byte[]? PdfBytes; // mały duplikat dla download speed
}
public class InvoiceCounter {
  Guid TenantId; int Year;
  long NextNumber; // atomic increment
}
```

**Migracja:** `20260527_AddInvoices`. Counter trzyma per-tenant per-year żeby uniknąć kolizji.

**Service:** `IInvoiceService` + `QuestPdfInvoiceGenerator`
- `Task<Invoice> CreateForRental(Guid rentalId)` — atomic increment counter, generuje PDF, zapisuje do blob
- `Task<byte[]> RegeneratePdf(Guid invoiceId)`
- `Task<bool> SendByEmail(Guid invoiceId)` — kiedy ruszą maile

**Auto-faktura:** flag `Tenant.AutoInvoiceOnReturn` — jeśli true, po `Status=Returned` automat tworzy invoice.

**Fakturownia (Etap 2):** `IFakturowniaClient` (REST), optional, jeśli `Tenant.FakturowniaApiKey` ustawiony to syncujemy invoice tam (jako external_id).

**UI:**
- `/admin/rentals/{id}` → przycisk "Wystaw fakturę" (Etap 1) albo automatycznie (Etap 2)
- `/admin/invoices` — lista wszystkich faktur z filtrami (data, klient, status), eksport CSV
- Client `RentalDetails.razor`: download PDF
- `/admin/company-settings` → tab "Faktury" (NIP, dane do faktury, opcjonalnie API key Fakturownia, format numeracji)

**Testy:**
- Unit: numeracja atomic (race conditions), VAT rounding (groszy)
- Integration: 100 paralelnych żądań → bez duplikatu numeru

**Estimate:** Etap 1 = 5 dni + 1 dzień testów. Etap 2 (Fakturownia) = +3 dni.

---

## FAZA 9 — komunikacja + standardowe wartości (Q2 2026, ~2 tygodnie)

🟡 **Wartość** — wyrównanie z Bookero w tym co partnerzy oczekują od strony "operacyjnej".

### 9a. Google Calendar 1-way push

**Co robimy:** Partner łączy konto Google przez OAuth2, wybiera kalendarz. Po `Confirmed` → tworzymy event w jego kalendarzu (klient, sprzęt, kwota, link do panelu).

**Entity:**
```csharp
public class TenantGoogleCalendarConnection {
  Guid Id; Guid TenantId; Guid ConnectedByUserId;
  string GoogleAccountEmail;
  string EncryptedRefreshToken; // DataProtection
  string CalendarId; // primary albo specific
  DateTime ConnectedAtUtc;
  DateTime? LastPushAtUtc;
  bool IsActive;
}
public class CalendarSyncMapping {
  Guid Id; Guid RentalId;
  string GoogleEventId;
  DateTime LastPushedAtUtc;
}
```

**Service:** `IGoogleCalendarService`
- `Task<string> GetOAuthUrl(Guid tenantId, string returnUrl)` — start flow
- `Task HandleCallback(Guid tenantId, string code)` — exchange + persist refresh token
- `Task PushEvent(Rental r)` — po Confirmed, w queue (background)
- `Task UpdateEvent(Rental r)` — po edycji
- `Task DeleteEvent(Rental r)` — po Cancelled

**OAuth scope:** `https://www.googleapis.com/auth/calendar.events` (najmniejszy konieczny).

**Background:** `CalendarPushHostedService` — czyta queue (in-process albo Postgres LISTEN/NOTIFY), retry-with-backoff przy 429/5xx.

**UI:**
- `/admin/integrations/google-calendar` — "Połącz konto Google" → redirect do OAuth, po powrocie pokazuje email + listę kalendarzy + select
- Status: Connected / Last sync / Disconnect button
- Per-rental w `Rentals.razor`: ikona "synced ✓" jeśli `CalendarSyncMapping` istnieje

**Testy:**
- Live test (memory `feedback_testing_llm`-style — ale tu Google, nie LLM): osobne konto test Google, scenariusze connect → push → verify w API → disconnect
- Skip in CI gdy brak `GOOGLE_OAUTH_CLIENT_ID`

**Estimate:** 5 dni + 2 dni testów.

---

### 9b. Outlook Calendar (MS Graph API)

**Analogicznie do 9a**, ale Microsoft Graph zamiast Google API. Refresh token via OAuth2 → push do `/me/events`.

**Estimate:** 3 dni (większość to copy-paste z 9a, różni się tylko klient HTTP i schema event'a).

---

### 9c. Kody rabatowe

**Entity:**
```csharp
public class DiscountCode {
  Guid Id; Guid TenantId;
  string Code; // unique per-tenant, case-insensitive
  DiscountType Type; // Percent | Fixed
  decimal Value;
  DateOnly? ValidFrom; DateOnly? ValidTo;
  int? MaxUses; int UsedCount;
  decimal? MinCartAmount;
  bool IsActive;
  string? Description;
}
public class DiscountUsage {
  Guid Id; Guid DiscountCodeId; Guid RentalId;
  DateTime UsedAtUtc;
}
```

**Migracja:** `20260603_AddDiscountCodes`

**Service:** `IDiscountService`
- `Task<DiscountValidationResult> Validate(string code, Guid tenantId, decimal cartAmount)`
- `Task<decimal> ApplyTo(decimal originalAmount, DiscountCode code)`
- Atomic increment `UsedCount` w transakcji checkoutu

**UI:**
- `/admin/discounts` — CRUD lista
- Client `Cart.razor`: pole "Kod rabatowy" + button "Zastosuj" → walidacja + pokazanie zniżki

**Estimate:** 3 dni + 1 dzień testów.

---

## FAZA 10 — marketing + retencja (Q3 2026, ~3 tygodnie)

🟡 **Wartość** — funkcje które trzymają klienta w naszym ekosystemie.

### 10a. Karnety / vouchery (pakiety)

**Use case:** wypożyczalnia narciarska sprzedaje "Karnet 7 dni jakichkolwiek nart" za 800 PLN. Albo SUP: "5 wynajmów 1h SUP" za 200 PLN.

**Entity:**
```csharp
public class VoucherTemplate {
  Guid Id; Guid TenantId;
  string Name; // "Karnet 7-dniowy"
  VoucherKind Kind; // Days | UseCount
  int Quantity; // 7 dni albo 5 użyć
  decimal Price;
  Guid? RestrictedCategoryId; // tylko narty
  int ValidityDays; // od kupna
  bool IsActive;
}
public class Voucher {
  Guid Id; Guid TenantId; Guid TemplateId; Guid CustomerId;
  string Code; // unique, np. "RS-VCH-A1B2C3"
  DateTime PurchasedAtUtc; DateTime? ExpiresAtUtc;
  int RemainingQuantity;
  VoucherStatus Status; // Active, Used, Expired, Refunded
}
public class VoucherRedemption {
  Guid Id; Guid VoucherId; Guid RentalId;
  DateTime RedeemedAtUtc;
  int QuantityUsed;
}
```

**Service:** `IVoucherService`
- `Task<Voucher> Purchase(Guid templateId, Guid customerId)` — Stripe payment osobno, generuje code
- `Task<VoucherValidationResult> Validate(string code, Rental rental)`
- `Task Redeem(string code, Guid rentalId)` — atomic decrement remaining

**UI:**
- `/admin/vouchers` — templates CRUD + lista sprzedanych vouchers + filter klient
- Client `/vouchers` — kupowanie (jak normalny checkout)
- Client `/cart` checkout: pole "Kod vouchera" → jeśli match, użycie zamiast płacenia
- Client `MyRentals.razor` → tab "Moje vouchery" z remaining

**Estimate:** 6 dni + 2 dni testów.

---

### 10b. Lista rezerwowa (waitlist)

**Use case:** klient chce SUP w sobotę 10:00, wszystkie zajęte. Wpisuje się na listę. Jak ktoś anuluje → email z 10-min ważnym magic linkiem do automatycznej rezerwacji.

**Entity:**
```csharp
public class WaitlistEntry {
  Guid Id; Guid TenantId; Guid CustomerId;
  Guid? ProductId; Guid? CategoryId; // jedno z dwóch
  DateTime DesiredFromUtc; DateTime DesiredToUtc;
  WaitlistStatus Status; // Waiting, Notified, Converted, Expired
  DateTime CreatedAtUtc;
  DateTime? NotifiedAtUtc;
  string? MagicLinkToken; // DataProtection
}
```

**Service:** `IWaitlistService` + `WaitlistMatchingHostedService`
- Trigger na: `Rental.Cancelled`, `Rental.Returned` (jeśli end_date wcześniej niż planowany)
- Matching algorithm: znajdź waitlist entries gdzie `desired_from..to` pokrywa się z uwolnionym oknem
- Wysłanie maila z magic linkiem (10 min TTL)
- Magic link → /waitlist/redeem/{token} → automatic rental creation w Stripe checkout

**UI:**
- Client `ProductDetails.razor`: jeśli zajęte → button "Powiadom mnie" → modal z date/time
- Client `MyAccount.razor` → tab "Moje powiadomienia" (lista waitlist)
- `/admin/waitlist` — lista wszystkich entries per-tenant

**Estimate:** 5 dni + 2 dni testów.

---

### 10c. Multi-language UX (PL/EN/DE/UA)

**Cel:** turyści w górach (Tatry: PL/UA/SK/EN) i nad morzem (Bałtyk: PL/DE/EN). Admin zostaje PL (właściciele wypożyczalni są polscy).

**Implementacja:**
- ASP.NET Localization: `IStringLocalizer<Page>` per komponent
- `.resx` per page (4 językowe pliki)
- Lang picker w `HomeLayout.razor` (PL flag itd.)
- Cookie `.AspNetCore.Culture` persistence
- Routing: `/{lang?}/products` — opcjonalny prefix
- Maile: template per lang (4×)

**Co tłumaczymy (Client tylko, na start):**
- Layout (header, footer, nav)
- Pages: Home, Products, ProductDetails, Cart, Checkout, MyRentals, Profile, Login, Register
- Email templates: rental confirmation, return reminder, review request

**Co NIE:**
- Admin (PL only)
- PDF umowy (PL only — ale w Etap 2 można dodać język klienta)

**Estimate:** 7 dni (głównie copywriting) + 1 dzień testów.

---

## FAZA 11 — skalowanie partnerów (Q3-Q4 2026, ~3 tygodnie)

🟢 **Scaling** — funkcje dla wypożyczalni 50+ wynajmów/mc albo wieloodziałowych.

### 11a. Multi-lokalizacja per-tenant

**Use case:** "Sport Karpacz" ma 3 punkty: centrum (rower + nart), góra (tylko nart), Las (tylko rower). Sprzęt nie pływa między punktami.

**Entity:**
```csharp
public class Location {
  Guid Id; Guid TenantId;
  string Name; string Address;
  decimal Lat; decimal Lng;
  string? PhoneNumber;
  Guid BusinessHoursScheduleId; // z fazy 8a
  bool IsDefault; bool IsActive;
}
```

**Migracja:** `20260910_AddLocations`. Dla każdego istniejącego tenanta tworzymy 1 default location z CompanyInfo. `Product.LocationId` + `Rental.LocationId` non-null FK z migracją populating na default.

**UI:**
- `/admin/locations` — CRUD
- `/admin/products/{id}` — pole "Lokalizacja" (mandatory)
- `/admin/rentals` — filter per-lokalizacja
- Client `/map` — pinezki per-location, nie per-tenant
- Client `Cart.razor` — pickup location selector (jeśli sprzęt w wielu lokalizacjach)

**Estimate:** 7 dni + 2 dni testów (sporo migracji).

---

### 11b. Prowizje pracowników

**Entity:**
```csharp
public class CommissionRate {
  Guid Id; Guid TenantId; Guid EmployeeId;
  Guid? CategoryId; // optional restriction
  CommissionType Type; // Percent | Flat
  decimal Value;
  DateOnly ValidFrom; DateOnly? ValidTo;
}
public class EarnedCommission {
  Guid Id; Guid EmployeeId; Guid RentalId;
  decimal Amount;
  DateTime EarnedAtUtc;
  CommissionStatus Status; // Pending, PaidOut
}
```

**Trigger:** po `Rental.Status = Returned` (rozliczone) → kalkulacja + zapis `EarnedCommission`.

**UI:**
- `/admin/employees/{id}` → tab "Prowizje" — lista CommissionRate + dodawanie
- `/admin/reports` → nowy raport "Prowizje per-pracownik" (miesiąc, custom okres)
- Pracownik (po loginie) widzi swoje prowizje w `/profile/commissions`

**Estimate:** 4 dni + 1 dzień testów.

---

### 11c. Wtyczka WordPress

**Plugin:** PHP, na WP Plugin Directory + GitHub.

```php
// rentspot-widget.php
[rentspot tenant="sport-karpacz"]
// → renderuje <iframe src="https://app.rentspot.eu/embed/sport-karpacz">
```

**Po stronie aplikacji:**
- `/embed/{slug}` — endpoint Razor z minimalnym layoutem (no nav, no footer, no auth)
- Tenant wybiera produkty wystawione w widgecie (nie wszystkie muszą być)
- postMessage do parent window dla resize iframe

**Estimate:** 5 dni (PHP plugin + Razor embed page + dokumentacja).

---

## FAZA 12 — polish + drobne (Q4 2026, ~2 tygodnie)

🟢 **Mały zakres**, ale uzupełnia parytet.

### 12a. Akceptacja ręczna rezerwacji

**Tenant flag** `RequiresManualApproval`. Po checkoutcie status = `PendingApproval` (zamiast Confirmed). Admin lista "Do zatwierdzenia" w `Rentals.razor` z buttonem Accept/Reject + powód. Email do klienta z decyzją.

**Estimate:** 2 dni.

---

### 12b. Wirtualna recepcja (admin global search)

**Strona** `/admin/concierge`:
- Search-box (full-text przez Postgres `tsvector`) across (klienci, wynajmy, produkty, faktury)
- Quick filters: data, status, kwota, klient
- One-click jump do szczegółów
- Useful gdy partner odbiera telefon "Pan Kowalski wczoraj wypożyczył rower, gdzie jego dane?"

**Estimate:** 3 dni.

---

## ZAKRES ŁĄCZNY

| Faza | Co | Estimate | Termin |
|---|---|---|---|
| 8a | Godziny pracy | 4 dni | tydz. 1 |
| 8b | Ceny sezonowe | 5 dni | tydz. 1-2 |
| 8c | Faktury VAT (Etap 1) | 6 dni | tydz. 2-3 |
| **Faza 8 razem** | | **~3 tyg** | **31 maja 2026** |
| 9a | Google Calendar | 7 dni | tydz. 4-5 |
| 9b | Outlook Calendar | 3 dni | tydz. 5 |
| 9c | Kody rabatowe | 4 dni | tydz. 5-6 |
| **Faza 9 razem** | | **~2.5 tyg** | **15 czerwca 2026** |
| 10a | Karnety/vouchery | 8 dni | tydz. 7-8 |
| 10b | Lista rezerwowa | 7 dni | tydz. 8-9 |
| 10c | Multi-language | 8 dni | tydz. 9-10 |
| **Faza 10 razem** | | **~3 tyg** | **5 lipca 2026** |
| 11a | Multi-lokalizacja | 9 dni | tydz. 11-12 |
| 11b | Prowizje | 5 dni | tydz. 12 |
| 11c | WordPress plugin | 5 dni | tydz. 13 |
| **Faza 11 razem** | | **~3 tyg** | **26 lipca 2026** |
| 12a | Manual approval | 2 dni | tydz. 14 |
| 12b | Wirtualna recepcja | 3 dni | tydz. 14 |
| **Faza 12 razem** | | **~1 tydz** | **2 sierpnia 2026** |

**Razem: ~13 tygodni netto** (3 miesiące, jeśli sprint po sprintcie bez przerw).

**Realistic launch partnerski:** koniec lipca 2026 z fazami 8 + 9 + 10 (parytet z Bookero Standard + Premium w 80%). Faza 11–12 leci parallel z onboardingiem partnerów.

---

## CO POMIJAMY ŚWIADOMIE (anti-pattern)

Z analizy konkurencji są funkcje Bookero, których **nie kopiujemy**:

| Funkcja Bookero | Dlaczego pomijamy |
|---|---|
| Rezerwacje cykliczne | Niska relevancja w wypożyczalni sportowej (nie regularny czynsz) |
| Google Meet / Teams integration | Wypożyczalnia nie umawia spotkań online |
| Bookero Builder strony www | Mamy publiczny katalog (Client) — partner ma własną www albo przekierowuje na nas |
| Plugin WebWave CMS | Niszowa platforma, robimy tylko WP (90% rynku PL SMB) |
| GetResponse integration | Email marketing to inny segment, partnerów to nie obchodzi w pierwszej fazie |

---

## METRYKI SUKCESU (po launchu)

Co będziemy mierzyć żeby zobaczyć czy parytet się opłacił:

1. **Conversion rate na demo:** % partnerów którzy po demo zostają na trialu (target: 25%)
2. **Trial → paid:** % partnerów którzy przechodzą z 60-dniowego free na płatny plan (target: 40%)
3. **Reasons for churn:** w wywiadach po anulowaniu — czy ktoś wskazuje brak konkretnej funkcji (jeśli "Bookero ma X a wy nie" pojawi się 3+ razy → priorytet)
4. **AI usage rate:** % partnerów którzy faktycznie korzystają z asystenta (>5 zapytań/tydzień; cel: 60%)
5. **Wynajmy/mc na partnera:** średnia z aktywnych partnerów po 3 miesiącach (target: 25/mc)

---

## RYZYKA + MITYGACJA

| Ryzyko | Prawdopodobieństwo | Wpływ | Mitygacja |
|---|---|---|---|
| **Faza 8 nie skończy się przed launchem** | 30% | Wysoki | Cut faza 8c (Fakturownia integration), zostaw tylko prosty PDF VAT |
| **Google Calendar OAuth approval delay** | 50% | Średni | Złożyć app verification od razu po implementacji 9a (Google: 4-6 tyg) |
| **Postgres scaling issues przy multi-loc** | 20% | Średni | Index na `(TenantId, LocationId)` w głównych tabelach od początku 11a |
| **Multi-language tłumaczenia źle zrobione** | 40% | Niski | Native speakers do review przed live (UA: znajomi z Tatr; DE: profesjonalne tłumaczenie) |
| **Stripe Connect dla marketplace fee** | 30% | Wysoki | Zostaje w fazie 13+ (post launch) — na razie subskrypcja partnera, nie % od transakcji |

---

## DECYZJE OTWARTE (do podjęcia w pierwszym tygodniu)

1. **Etap 2 fakturownia (faza 8c)?** Tak/nie — czy partnerzy mają już Fakturownia. Quick survey 10 partnerów z bazy.
2. **Cykl rachunkowy faktur:** netto + VAT calculator nasz, czy zostawić partnerowi? (W naszym kraju: VAT od wynajmu = 23%, ale są wyjątki).
3. **Stripe Connect (marketplace) vs simple subscription?** — wpływa na cennik. Decyzja wymaga rozmowy z prawnikiem.
4. **Multi-currency?** PLN tylko czy też EUR (turyści)? — jeśli EUR, to dodatkowa złożoność w ProductPrice + invoicing.

---

*Plan: 2026-05-07. Iteracje commit-per-faza, deploy do `srental2`, smoke prod między fazami. Aktualizujemy po każdym sprincie.*
