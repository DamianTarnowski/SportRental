# Bookero vs RentSpot — analiza porównawcza

> Stan: 2026-05-07. Źródła: [bookero.pl](https://www.bookero.pl), [bookero.pl/funkcje](https://www.bookero.pl/funkcje), [bookero.pl/cennik](https://www.bookero.pl/cennik) (cytaty dosłowne); RentSpot — bezpośredni przegląd kodu repo `SportRentalHybrid` (Admin, Client, Infrastructure, Shared).

---

## TL;DR

**Bookero** to horyzontalny SaaS do rezerwacji online dla 9 branż (florystyka, gastronomia, motoryzacja, jachty, uroda, domki, medycyna, samochody, escape roomy) z mocnym naciskiem na **kalendarz + płatność + powiadomienie**. Jest dojrzały marketingowo, dobrze udokumentowany, z gotowym cennikiem i 14-dniowym trialem.

**RentSpot** to wertykalny system dla **wypożyczalni sprzętu sportowego** — wszystko co Bookero w tej branży, plus rzeczy których Bookero **nie ma w ogóle** (umowa PDF z brandingiem, kaucje, kody kreskowe na sprzęcie, customer trust scoring, multi-tenant marketplace, asystent AI, hand-over flow z weryfikacją wydania/zwrotu).

**Kluczowa pozycja**: nie konkurujemy "lepszym kalendarzem" — konkurujemy **kompletnością procesu wypożyczenia** (rezerwacja → umowa → wydanie → zwrot → ocena → zaufanie). Bookero zatrzymuje się na "rezerwacja + płatność"; my idziemy do końca cyklu.

---

## 1. Kim jest Bookero

**Tagline:** "Klik i gotowe!" / "Zarabiaj więcej, sprawniej obsługuj swoje rezerwacje i oszczędzaj czas".

**Model:** SaaS, miesięczna subskrypcja per-firma. Self-service, bez prowizji od transakcji (płatność idzie bezpośrednio do operatora wybranego przez klienta — TPay, Stripe, PayU, Przelewy24 itd.).

**Branże wymienione na stronie:** florystyka, gastronomia, motoryzacja, wynajem jachtów, uroda, wynajem domków, medycyna, wynajem samochodów, escape room.
> **Wypożyczalnie sportowe nie są wymienione jako osobny segment** — czyli to Bookero jest tu w roli "ogólnego silnika rezerwacji", nie wertykalu.

**Funkcjonalność zewnętrzna (39 udokumentowanych funkcji):**

| # | Funkcja | Cytat |
|---|---|---|
| 1 | Koszyk rezerwacji | "Spraw aby rezerwacje były jeszcze łatwiejsze dzięki funkcji koszyka." |
| 2 | Reguły rezerwacji | Limity i warunki |
| 3 | Reguły cen | "Stwórz własny cennik – również bardziej rozbudowany." |
| 4 | Reguły otwarcia | Harmonogramy, dni wolne |
| 5 | Parametry rezerwacji | Pola tekstowe + listy w formularzu |
| 6 | Pełen zakres usług | Typy usług + ceny + grafiki |
| 7 | Rezerwacje cykliczne | "Ustalonych przez administratora" |
| 8 | Kody rabatowe | Listy kodów procentowych |
| 9 | Płatności online | TPay/Stripe/PayU/PayPal/iMoje/Przelewy24/HotPay/Dotpay/Paynow |
| 10 | Strona www | Builder strony jeśli firma jej nie ma |
| 11 | Widoki formularza | Sticky / Inline / Weekly / Monthly |
| 12 | Konta pracowników | Każdy pracownik widzi swoje rezerwacje |
| 13 | Lista klientów | Auto-profil klienta + historia rezerwacji |
| 14 | Wirtualna recepcja | Wyszukiwarka wolnych terminów + historia |
| 15 | Integracje wtyczek | WordPress + WebWave CMS |
| 16 | Moduł RODO | Specjalny moduł zgód |
| 17 | Powiadomienia email | Własna treść |
| 18 | Powiadomienia SMS | Od 0,15 PLN za sztukę |
| 19 | Anulowanie przez klienta | Self-service cancel |
| 20 | Historia płatności | Z filtrami |
| 21 | Kalendarze | Różne widoki + filtry |
| 22 | Podsumowanie dnia | Obłożenie pracowników |
| 23 | Raporty Excel | Eksport rezerwacji |
| 24 | Wyszukiwarka terminów | Po datach |
| 25 | Niestandardowe terminy | Ręczne wprowadzanie poza godzinami |
| 26 | Personalizacja | "Przebudować istniejące rozwiązania" |
| 27 | Google + Outlook Calendar | Dwukierunkowa synchronizacja |
| 28 | Wersje językowe | Multi-lang formularz + maile |
| 29 | Rezerwacja z wyprzedzeniem | Future booking |
| 30 | Lista rezerwowa | Waitlist gdy brak miejsc |
| 31 | Akceptacja ręczna/auto | Konfigurowalne |
| 32 | Ankiety | Auto-wysyłka po wizycie |
| 33 | Integracja GetResponse | Mailing |
| 34 | Integracja z fakturownią | Auto-generowanie faktur |
| 35 | Google Meet + Teams | Auto-link na spotkania online |
| 36 | Rezerwacje na dni | Doby + godziny |
| 37 | Wybór czasu trwania | Przez klienta |
| 38 | Lista obecności | Potwierdzenie odbycia / no-show |
| 39 | Bilety z kodem QR | "Szybko weryfikuj dane rezerwacji dzięki zeskanowaniu kodu QR" |

**Cennik (dosłowny, netto, +23% VAT, 14-dniowy free trial):**

| Plan | Miesięcznie | Z rabatem 12-mc (-10%) | Limit pracowników | Co dochodzi |
|---|---|---|---|---|
| **Basic** | 19,90 PLN | 17,91 PLN | 1 (admin) | Rezerwacje, reguły, harmonogram, usługi, klienci, płatności online |
| **Standard** | 49,90 PLN | 44,91 PLN | do 3 | Prowizje pracowników, rezerwacje na dni, custom pola, builder www, **rezerwacje cykliczne**, **SMS** (0,15+ PLN), **ankiety**, **fakturownia** |
| **Premium** | 89,90 PLN | 80,91 PLN | do 200 | **Karnety**, **bilety QR**, reguły cen + rabaty, Google/Outlook Calendar, multi-lang, ukrycie stopki "Bookero" w pluginach |

**Co Bookero promuje jako "wyróżnik":**
- Zgodność z RODO (osobny moduł zgód)
- 14 dni free bez karty kredytowej
- 20% zniżki na stronę www u partnerów
- Niższa prowizja przy współpracy z TPay (niesprecyzowana)

---

## 2. Kim jest RentSpot (stan na 2026-05-07, branch `main`)

**Tagline (wg materiałów Macieja):** "Marketplace wynajmu sprzętu sportowego" / "Airbnb dla sprzętu sportowego".

**Model:** dwustronny — **panel partnera** (właściciel/pracownik wypożyczalni) + **panel klienta końcowego** (osoba wypożyczająca). Multi-tenant: jedna instancja, każdy partner = osobny `Tenant`, klient widzi globalny katalog ale rezerwuje per-tenant. Stripe checkout z idempotency key, webhook jako primary source of truth.

**Stack:** .NET 10, Blazor Server (Admin) + WebAssembly (Client), MudBlazor, EF Core 9, PostgreSQL, Azure App Service, Azure Key Vault, Azure OpenAI Foundry (gpt-5.5 + gpt-realtime), QuestPDF, BarcodeLib, Stripe.net, Azure Blob Storage. Hosting na Azure (`srental2.azurewebsites.net`, docelowo `app.rentspot.eu`).

### Strony Admin (panel partnera) — `/admin/*`

| Route | Co robi |
|---|---|
| `/` | Hero panelu z quick actions (nowy wynajem, produkty, klienci, dashboard) |
| `/dashboard` | Dziś do wydania, dziś do zwrotu, aktywne wynajmy, klienci, wykres przychodu |
| `/admin/products` | CRUD katalogu sprzętu, kategorie, kaucja, cena/dzień, zdjęcia, kody (barcode) |
| `/admin/rentals` | Lista wynajmów, statusy (Draft/Pending/Confirmed/Active/Returned/Cancelled), filtry, dialog tworzenia |
| `/admin/customers` | Baza klientów + trust score, historia, blokady |
| `/admin/employees` | Zaproszenia pracowników (email-link), role + permissions |
| `/admin/schedule` | Kalendarz + Gantt-style obłożenie sprzętu |
| `/admin/equipment-handling` | Hand-over flow: wydanie + zwrot ze skanerem |
| `/admin/barcode-scanner` + `/admin/qr-scanner` | Skaner + lookup wynajmu po SKU |
| `/admin/reports` | Raporty: przychód, top klienci, opóźnienia, prognoza popytu |
| `/admin/reviews` | Oceny od klientów (po wynajmie) |
| `/admin/feedback` | Feedback od użytkowników panelu (sugestie + bugi) |
| `/admin/contract-template` | Edytor szablonu umowy z placeholderami |
| `/admin/company-settings` | Dane firmy (NIP, REGON, logo, regulamin) |
| `/admin/owner` | Panel właściciela tenanta |
| `/admin/super` | SuperAdmin: cross-tenant view, zaproszenia owner, billing platformy |
| `/admin/chat-settings` | Konfiguracja AI asystenta (model, write-tools) |
| `/ankieta/{rentalId}` | Public ankieta opinii (token DataProtection, bez logowania) |

### Strony Client (panel klienta) — `/`

| Route | Co robi |
|---|---|
| `/` | Landing, hero, "wszystkie wypożyczalnie w okolicy" |
| `/products` | Globalny katalog wszystkich tenantów + filtry |
| `/products/{id}` | Szczegóły produktu, dodaj do koszyka |
| `/map` | Mapa wypożyczalni (Leaflet, OpenStreetMap) |
| `/cart` | Koszyk multi-tenant (sprzęt z różnych wypożyczalni) |
| `/checkout` | Dane klienta + Stripe checkout |
| `/checkout/success`, `/checkout/cancel` | Powrót ze Stripe |
| `/my-rentals` | Historia wynajmów zalogowanego klienta |
| `/my-rentals/{id}` | Szczegóły + faktura PDF |
| `/reviews` + `/reviews/opt-out` | Wystawienie opinii / rezygnacja z e-mailowej prośby |
| `/select-tenant` | Wybór wypożyczalni (gdy klient ma wynajmy w wielu) |
| `/profile`, `/account`, `/login`, `/register` | Konto klienta |

### Funkcje "core" (z kodu)

**A. Rezerwacja + checkout**
- Multi-tenant koszyk (sprzęt z różnych wypożyczalni w jednej transakcji Stripe)
- Stripe Checkout Sessions z `IdempotencyKey` w metadata
- Webhook `/api/payments/webhook` jako source of truth (HMAC weryfikacja)
- `ReservationHold` — temporary hold sprzętu na czas checkoutu (cleaner background service `ExpiredHoldsCleaner`)
- Anonymous checkout (klient nie musi zakładać konta)
- Czas wynajmu w godzinach lub dniach

**B. Sprzęt + katalog**
- `Product` z polami: SKU, kategoria, kaucja (`DepositAmount`), cena/dzień, dostępność, zdjęcia (warianty rozmiarów), `BarcodeValue`
- `BarcodeGenerator` (Code 128, BarcodeLib) — etykiety A4 z dialogu `QrLabelsDialog`
- `SimpleQrCodeGenerator` (deprecated od 2026-04, zostawiony dla legacy)
- Multi-foto z cropper, S3/Azure Blob storage abstrakcja (`IFileStorage`)

**C. Hand-over flow (wydanie/zwrot)**
- Dialog `IssueEquipmentDialog` — wydanie ze skanowaniem kodu kreskowego, podpis cyfrowy, foto stanu
- Dialog `ReturnEquipmentDialog` — zwrot, weryfikacja stanu, zwrot kaucji, opcjonalnie potrącenie
- `RentalConfirmationService` — potwierdzenie SMS od klienta (kod 4-cyfrowy) przed wydaniem
- Audit log każdej akcji (`DatabaseAuditLogger`)

**D. Umowy + dokumenty**
- `QuestPdfContractGenerator` — PDF umowy z brandingiem RentSpot + brandingiem partnera, header + paragrafy + tabela sprzętu + stopka
- `ContractTemplate` — edytowalny szablon per-tenant z placeholderami `{{CustomerName}}`, `{{StartDate}}`, `{{ItemsTable}}` itd.
- Historia umów per-rental, link do downloadu w Client

**E. Zaufanie klienta (unikalne)**
- `CustomerTrustCalculator` — score 0–100 na bazie: historii (zakończone wynajmy), incydentów (uszkodzenia/opóźnienia), średniej oceny od pracowników, manualnych blokad
- Statusy: `Unverified` / `Trusted` / `Flagged` / `Blocked`
- `CustomerTrustBadge` widoczny w listach wynajmów i dialogach
- Pracownik może oznaczyć incydent (uszkodzenie sprzętu, brak zwrotu, agresja)
- Pracownik wystawia ocenę klientowi po zwrocie (`RentalItemReview` per-pozycja)

**F. Powiadomienia**
- SMS: 3 providery z routingiem (`SmsApiSender` SMSAPI.pl, `SerwerSmsSender` SerwerSMS, `ConsoleSmsSender` dev), wybór per-tenant
- Email: `SmtpEmailSender` (MailKit), templates HTML
- Confirmation kod SMS przed potwierdzeniem rezerwacji
- Email przypomnienie zwrotu (`RentalReminderService`, hosted background)
- Email prośba o opinię (`ReviewRequestService`, X dni po zwrocie, z opt-out tokenem)

**G. Wieloosobowość (multi-tenant + permissions)**
- `Tenant` = wypożyczalnia (CompanyInfo: NIP, adres, logo, regulamin, kolory marki)
- `TenantUser` z rolami: Owner, Manager, Employee
- `EmployeePermissions` — granularne flagi (canCreateRental, canIssueEquipment, canViewReports itd.)
- `EmployeeInvitation` z linkiem-tokenem (DataProtection-protected)
- `ITenantProvider` w EF query filters (`x.TenantId == null || x.TenantId == TenantId`) — SuperAdmin widzi wszystko, klient widzi global, pracownik widzi swój tenant

**H. Asystent AI (unikalne, brak u Bookero)**
- `OpenAiChatService` — gpt-5.5 z function calling, multi-turn, 60s hard cap
- `ReadToolService` — 13+ narzędzi read-only: count_active_rentals, get_today_rentals, get_overdue_rentals, get_pending_actions, get_revenue_summary, search_rentals, get_customer_trust, get_customer_history, find_rental_by_sku, get_top_customers, get_employee_list, forecast_demand
- `WriteToolService` — narzędzia write z confirmation flow (preview → user accept → execute): update_customer_notes, mark_rental_as_paid, create_rental_draft itd.
- `ChatPersistenceService` — historia rozmów per-user, cross-session memory ("ostatnio pytałeś o X")
- `FloatingChatService` — chat-bubble w prawym dolnym rogu, dostępny na każdej stronie panelu
- Voice mode: WebRTC + Azure Foundry `gpt-realtime` (Sweden Central) — pełna rozmowa głosowa z asystentem
- `AiChatSettings` — SuperAdmin może wymusić model defaultowy lub pozwolić user wybrać
- `FeedbackService` — kciuki w górę/dół na każdą odpowiedź → SuperAdmin feedback list

**I. Płatności**
- Stripe Checkout (Card + Apple Pay + Google Pay)
- Kaucja jako `DepositPaid` po checkoutcie (`Rental.PaymentStatus`), reszta przy wydaniu
- Webhook idempotentny + retry-safe (deduplikacja po `IdempotencyKey` w `Rental`)

**J. Bezpieczeństwo (unikalne)**
- IDOR-resistant endpoints: `/api/contracts/{id}`, `/api/products/{id}/image`, `/api/tenants/{id}/logo` z ownership check (`IgnoreQueryFilters` + porównanie tenant z claims)
- JWT + ASP.NET Identity, refresh tokens, 2FA opcjonalnie
- DataProtection tokens dla public links (ankieta, opt-out, employee invitation)
- Rate limiting (.NET 10 native)
- Repo public → sekrety w Azure Key Vault z DefaultAzureCredential

---

## 3. Macierz porównawcza (head-to-head)

Legenda: ✅ jest, ⚠️ częściowo / inny zakres, ❌ brak.

### Rezerwacje + kalendarz

| Funkcja | Bookero | RentSpot | Komentarz |
|---|:---:|:---:|---|
| Rezerwacja na dni | ✅ | ✅ | Parytet |
| Rezerwacja na godziny | ✅ | ✅ | Parytet |
| Koszyk rezerwacji | ✅ | ✅ | RentSpot dodatkowo: koszyk **multi-tenant** (sprzęt z różnych wypożyczalni w jednej transakcji) |
| Lista rezerwowa (waitlist) | ✅ | ❌ | **Brakuje nam** |
| Rezerwacje cykliczne | ✅ | ❌ | Mała relevancja w wypożyczalni sportowej, ale można dodać |
| Akceptacja ręczna/auto | ✅ | ⚠️ | Mamy tylko auto; ręczna akceptacja nie jest zaimplementowana |
| Wyszukiwarka terminów po datach | ✅ | ⚠️ | Klient widzi dostępność per-produkt, brak globalnego "kiedy wolne" |
| Reguły otwarcia (godziny pracy) | ✅ | ❌ | **Brakuje nam** — wypożyczalnia może chcieć blokować rezerwacje poza godzinami |
| Niestandardowe terminy (admin override) | ✅ | ✅ | Admin może utworzyć wynajem ręcznie poza standardem |
| Anulowanie przez klienta | ✅ | ✅ | Parytet (z ograniczeniem czasowym przed startem) |
| Widok kalendarza (day/week/month) | ✅ | ✅ | Mamy `/admin/schedule` |
| Synchronizacja Google/Outlook Calendar | ✅ (Premium) | ❌ | **Brakuje** — partner nie zobaczy wynajmu w swoim kalendarzu Google |

### Klient i CRM

| Funkcja | Bookero | RentSpot | Komentarz |
|---|:---:|:---:|---|
| Auto-profil klienta | ✅ | ✅ | Parytet |
| Historia rezerwacji per-klient | ✅ | ✅ | Parytet |
| **Trust score / blokady** | ❌ | ✅ | **Nasz wyróżnik** — Bookero traktuje wszystkich klientów jednakowo |
| **Incydenty / flagi** | ❌ | ✅ | **Nasz wyróżnik** |
| **Ocena klienta przez pracownika** | ❌ | ✅ | **Nasz wyróżnik** (`RentalItemReview`) |
| Lista obecności (potwierdzenie/no-show) | ✅ | ⚠️ | U nas to flow wydania/zwrotu, nie osobna lista |
| Moduł RODO (zgody) | ✅ | ⚠️ | Mamy zgody w rejestracji + opt-out, brak osobnego "modułu" UI |
| Ankiety post-wizyta | ✅ | ✅ | Mamy `RentalSurvey` z public tokenem |

### Płatności

| Funkcja | Bookero | RentSpot | Komentarz |
|---|:---:|:---:|---|
| Płatność online | ✅ | ✅ | Parytet |
| Liczba operatorów | 9+ (TPay, Stripe, PayU, PayPal, P24, iMoje, Dotpay, Paynow, HotPay) | 1 (Stripe) | Bookero ma większy wybór; Stripe pokrywa Card/Apple Pay/GPay globalnie |
| **Kaucja (deposit)** | ❌ | ✅ | **Nasz wyróżnik** — Bookero nie ma konceptu kaucji oddzielnej od ceny |
| Idempotency / webhook source-of-truth | ⚠️ | ✅ | Bookero nie publikuje szczegółów; my mamy webhook + IdempotencyKey + dedup |
| Faktury (auto-gen) | ✅ (integracja Fakturownia) | ❌ | **Brakuje** |
| Karnety / vouchery | ✅ (Premium) | ❌ | **Brakuje** — relevant np. dla wypożyczalni narciarskiej z 7-dniowym karnetem |
| Kody rabatowe | ✅ | ❌ | **Brakuje** |
| Reguły cen (sezonowe) | ✅ (Premium) | ⚠️ | Mamy stałą cenę/dzień, brak cennika "wysoki sezon vs. niski" |

### Wynajem sprzętu (specyficzne dla branży)

| Funkcja | Bookero | RentSpot | Komentarz |
|---|:---:|:---:|---|
| **Katalog sprzętu z SKU** | ❌ | ✅ | **Nasz wyróżnik** — Bookero ma "usługi", nie egzemplarze |
| **Kody kreskowe na sprzęcie** | ❌ | ✅ | **Nasz wyróżnik** (Code 128, BarcodeLib, etykiety A4) |
| **Skaner kodów na panelu** | ⚠️ (tylko bilety QR rezerwacji) | ✅ | Bookero skanuje **rezerwację**, my skanujemy **konkretny egzemplarz** sprzętu |
| **Hand-over flow (wydanie/zwrot)** | ❌ | ✅ | **Nasz wyróżnik** — pełen flow z dialogami `IssueEquipmentDialog`/`ReturnEquipmentDialog` |
| **Umowa PDF z brandingiem** | ❌ | ✅ | **Nasz wyróżnik** (QuestPDF) |
| **Edytor szablonu umowy** | ❌ | ✅ | **Nasz wyróżnik** (`/admin/contract-template`) |
| **Foto stanu sprzętu (wydanie/zwrot)** | ❌ | ✅ | **Nasz wyróżnik** |
| **Confirmation SMS przed wydaniem** | ❌ | ✅ | **Nasz wyróżnik** |
| **Multi-foto produktu z cropperem** | ⚠️ | ✅ | Bookero ma jakieś zdjęcia w usłudze, my mamy galerię + warianty |

### Pracownicy + organizacja

| Funkcja | Bookero | RentSpot | Komentarz |
|---|:---:|:---:|---|
| Konta pracowników | ✅ | ✅ | Parytet |
| Limit pracowników (Premium) | 200 | ∞ | My nie limitujemy |
| Granularne permissions | ⚠️ | ✅ | Mamy `EmployeePermissions` (12+ flag) |
| Zaproszenia email-link | ⚠️ | ✅ | Mamy `EmployeeInvitation` z DataProtection token |
| Prowizje pracowników | ✅ (Standard) | ❌ | **Brakuje** |
| Multi-lokalizacja | ⚠️ | ⚠️ | Bookero ma "Listę pracowników z grafikami"; my mamy multi-tenant ale jeden tenant = jedna lokalizacja na razie |

### Powiadomienia + komunikacja

| Funkcja | Bookero | RentSpot | Komentarz |
|---|:---:|:---:|---|
| Powiadomienia email | ✅ | ✅ | Parytet |
| Powiadomienia SMS | ✅ (Standard, 0,15+ PLN) | ✅ (3 providery: SMSAPI, SerwerSMS, w opcji router) | Parytet, my mamy więcej providerów |
| Multi-lang formularz + maile | ✅ (Premium) | ⚠️ | Mamy PL, EN — ale formularz Client jest tylko PL |
| Kampanie / mailing | ✅ (integracja GetResponse) | ❌ | **Brakuje** — można dorzucić Resend/Mailchimp jeśli zajdzie potrzeba |

### Integracje + ekosystem

| Integracja | Bookero | RentSpot |
|---|:---:|:---:|
| WordPress | ✅ | ❌ |
| WebWave CMS | ✅ | ❌ |
| Google Calendar | ✅ | ❌ |
| Outlook Calendar | ✅ | ❌ |
| Google Meet | ✅ | ❌ (irrelevantne dla wypożyczalni) |
| Microsoft Teams | ✅ | ❌ (irrelevantne) |
| GetResponse | ✅ | ❌ |
| Fakturownia | ✅ | ❌ |
| Stripe | ✅ | ✅ |
| TPay/PayU/Przelewy24 | ✅ | ❌ |
| PayPal | ✅ | ⚠️ (przez Stripe) |
| iMoje/Dotpay/HotPay/Paynow | ✅ | ❌ |

### AI / inteligencja

| Funkcja | Bookero | RentSpot |
|---|:---:|:---:|
| **Asystent AI w panelu** | ❌ | ✅ |
| **Read-only tools (zapytania natural-language)** | ❌ | ✅ (13+ narzędzi) |
| **Write tools z confirmation flow** | ❌ | ✅ |
| **Voice mode (WebRTC + GPT realtime)** | ❌ | ✅ |
| **Forecast popytu** | ❌ | ✅ (`forecast_demand`) |
| **Cross-session memory rozmów** | ❌ | ✅ |

### Marketplace / multi-vendor

| Funkcja | Bookero | RentSpot |
|---|:---:|:---:|
| **Wieloma wypożyczalniami w jednej aplikacji klienta** | ❌ | ✅ |
| **Globalny katalog (klient widzi wszystkie wypożyczalnie)** | ❌ | ✅ |
| **Mapa wypożyczalni** | ❌ | ✅ (`/map`, Leaflet + OSM) |
| **Multi-tenant koszyk** | ❌ | ✅ (sprzęt z różnych wypożyczalni → jedna płatność Stripe) |
| **SuperAdmin cross-tenant** | ❌ | ✅ |

---

## 4. Mocne strony Bookero (czego nam brakuje)

W kolejności priorytetu (subiektywnie, dla docelowego rynku 1330 wypożyczalni z bazy):

1. **Dojrzałość marketingowo-sprzedażowa** — strona, cennik, 14-dniowy trial bez karty, materiały dla SEO. Mamy dystans w komunikacji do klienta końcowego (partnera).
2. **Faktury (Fakturownia)** — dla każdej wypożyczalni biznesowej to **must-have**. Bez tego księgowa partnera nie będzie zadowolona. Priorytet wysoki.
3. **Synchronizacja kalendarzy Google/Outlook** — partner przyjmuje "zwykłe" rezerwacje (nie tylko sprzętu) i chce widzieć wszystko w jednym kalendarzu. Średni priorytet.
4. **Reguły otwarcia (godziny pracy + dni wolne)** — bez tego klient może rezerwować na 03:00 w niedzielę. Priorytet średni-wysoki.
5. **Lista rezerwowa / waitlist** — klient: "powiadom mnie gdy ten rower będzie wolny". Średni priorytet.
6. **Reguły cen (sezonowość)** — wysoki sezon (lipiec-sierpień nad morzem, grudzień-luty w górach). Bardzo relevantne dla branży. Wysoki priorytet.
7. **Karnety / vouchery** — relevantne dla narciarskich (7-dniowy karnet) i SUP/kajaki (5 wynajmów w pakiecie). Średni priorytet.
8. **Kody rabatowe** — standard. Średni priorytet.
9. **Wtyczka WordPress** — wiele wypożyczalni ma już stronę WP, chcą tylko widget rezerwacji. Średni priorytet.
10. **Multi-language UX** — klienci zagraniczni w turystycznych miejscach (Mazury, Tatry, Bałtyk). Niski-średni priorytet.
11. **Prowizje pracowników** — duże wypożyczalnie z 5+ pracownikami. Niski priorytet (mała część rynku).
12. **Bookero Plugin Marketplace / Custom builder strony** — alternatywa dla partnerów bez własnej www.

## 5. Mocne strony RentSpot (czego oni nie mają)

W kolejności od największego differentiator:

1. **Pełen flow wynajmu sprzętu** (rezerwacja → umowa PDF → kaucja → wydanie ze skanerem → zwrot ze stanem → ocena) — Bookero zatrzymuje się na "rezerwacja + płatność".
2. **Customer trust scoring** — niespotykane w SaaS rezerwacyjnym. Wypożyczalnia sprzętu jest narażona na uszkodzenia/kradzieże, system zaufania = realna wartość biznesowa.
3. **Marketplace dwustronny** — Bookero to zawsze 1 firma → wielu klientów. My: wiele wypożyczalni → wszyscy klienci → możliwość mieszanego koszyka.
4. **Asystent AI** — natural-language queries do bazy ("ile mam zaległych zwrotów?", "top 5 klientów"), forecast popytu, voice mode. Konkurencja ma maksymalnie chatboty FAQ.
5. **Kody kreskowe na sprzęcie** (nie na bilecie rezerwacji!) — fundamentalna różnica. Skanujemy fizyczny egzemplarz roweru, nie token rezerwacji.
6. **Hand-over flow z foto stanu** — istotne dla rozliczania uszkodzeń kaucji.
7. **Multi-tenant koszyk** — klient w Tatrach: rower z wypożyczalni A + kask z wypożyczalni B → jedna płatność.
8. **Edytowalny szablon umowy z brandingiem** — partner customizuje pod swoje potrzeby (lokalna jurysdykcja, branżowe klauzule).
9. **Niski koszt operacyjny** — multi-tenant na jednej instancji Azure App Service B2 = ~$30/mc dla N partnerów. Pozwala na agresywny pricing albo % od transakcji.
10. **Bezpieczeństwo na poziomie produkcyjnym** (audyt IDOR, webhook idempotent, repo public z secrets w Key Vault) — Bookero ma "moduł RODO", my mamy realne mechanizmy.
11. **Open source / public repo** — partner może zobaczyć kod, partner techniczny może self-hostować (długoterminowy plus dla zaufania).

---

## 6. Cennik — porównanie i propozycja

**Bookero:**
- Basic: 19,90 / 17,91 PLN/mc (1 pracownik)
- Standard: 49,90 / 44,91 PLN/mc (3 pracowników)
- Premium: 89,90 / 80,91 PLN/mc (200 pracowników)
- + SMS od 0,15 PLN
- 14-dniowy trial bez karty

**RentSpot — propozycja (nie zatwierdzona):**

| Plan | Cena | Co dostaje |
|---|---|---|
| **Free / Pilot** | 0 PLN przez 60 dni | Wszystkie funkcje, bez limitów, dla pierwszych N partnerów |
| **Starter** | 39 PLN/mc | 1 lokalizacja, 1 pracownik, do 50 wynajmów/mc, AI asystent (read-only), bez % od transakcji |
| **Pro** | 99 PLN/mc | 1 lokalizacja, do 5 pracowników, bez limitu wynajmów, AI write tools, voice mode, custom domain, faktury (gdy wpadnie), bez % |
| **Business** | 199 PLN/mc | Multi-lokalizacja, ∞ pracowników, priorytet supportu, training, custom branding (logo zamiast RentSpot), bez % |
| **Marketplace fee** | 0% albo 2% | Opcjonalnie: 0% w Pro/Business, 2% w Starter (zachęta do upgrade) |

**Dlaczego niżej niż Bookero w niektórych miejscach:**
- AI asystent jest naszym wyróżnikiem od dnia pierwszego, włączamy go już w Starter
- Brak limitu pracowników szybciej (od 99 PLN, nie 89,90)
- Marketplace fee (alt-revenue stream) pozwala na agresywniejszy pricing subskrypcji

**Dlaczego wyżej niż Bookero Basic:**
- Nasz Starter (39 PLN) > Bookero Basic (19,90 PLN), ALE: Bookero Basic to 1 pracownik **bez SMS, bez Google Calendar, bez QR, bez ankiet** — czyli realnie funkcjonalnie nasz Starter konkuruje z Bookero Standard (49,90 PLN). Jesteśmy 20% taniej.

**Co warto rozważyć:**
- Free trial bez karty kredytowej (zgodnie z Bookero) — niski friction onboarding
- Yearly discount ~15% (tak jak Bookero -10%)
- **Wsparcie partnerów** w pierwszym tygodniu (Maciej osobiście) — Bookero tego nie robi, to nasz różnicator dla małych wypożyczalni

---

## 7. Pozycjonowanie strategiczne

### Headline RentSpot vs. Bookero

**Bookero pozycjonuje się tak:**
> "System rezerwacji online dla każdej branży. Klik i gotowe!"
> = generalista, "rezerwacja czegokolwiek"

**RentSpot powinien pozycjonować się tak:**
> "System dla wypożyczalni sprzętu sportowego. Od rezerwacji po zwrot — wszystko w jednym miejscu."
> = wertykal, "wszystko czego wypożyczalnia potrzebuje, bookero tego nie umie"

### Argumenty sprzedażowe (battlecard) wobec Bookero

| Sytuacja | Argument |
|---|---|
| "Mam już Bookero, po co mi zmiana?" | "Bookero kończy na rezerwacji. Co robi gdy klient zwraca rower z zarysowaniami? My mamy zdjęcia, kaucje, oceny klienta. Bookero — nic." |
| "Bookero jest tańszy" | "Tak, Bookero Basic 19,90 PLN. Ale bez SMS, bez QR, bez ankiet. Z dodatkami płacisz 49,90 — tyle samo co my, a my dostajemy AI asystenta i pełen flow wynajmu." |
| "Mam już bazę klientów w Bookero" | "Migracja CSV w 1 dzień. Zachowamy Twoją historię. Bonus: trust score retroaktywnie wyliczony z historii." |
| "Bookero ma więcej operatorów płatności" | "Stripe pokrywa karty, Apple Pay, Google Pay, BLIK przez P24 (w roadmapie). 95% Twoich klientów to obsłuży." |
| "Mam tylko 1 pracownika i 20 wynajmów na miesiąc" | "Plan Starter 39 PLN/mc — to dokładnie tyle co Bookero Standard. Plus AI asystent. Plus pełna umowa PDF. Bookero tego nie ma." |

### Czego NIE robić (anti-pattern)

1. **Nie kopiować Bookero 1:1.** Nasz wyróżnik to wertykal — dodajemy funkcje branżowo, nie horyzontalnie. (Nie róbmy "rezerwacji do dentysty" tylko dlatego że Bookero ma).
2. **Nie konkurować ceną w Bookero Basic** (19,90 PLN). Tam jest beznadziejny segment self-service hobbystów. Nasz target to zawodowe wypożyczalnie z >50 wynajmami/mc.
3. **Nie walczyć integracjami WordPress jako pierwszą rzeczą.** Bookero ma 5-lat lead w pluginach. Najpierw flagship feature (AI + trust + hand-over), potem ekosystem.
4. **Nie obiecywać "wszystkiego dla każdego".** Bookero nas tu pokona. Jesteśmy "dla wypożyczalni sportowej" i nigdzie indziej (na razie).

---

## 8. Roadmapa rekomendowana (z perspektywy konkurencji)

### Q2 2026 (przed launchem partnerskim)
- ✅ Rebrand RentSpot (zrobione)
- ⏳ Subdomena app.rentspot.eu (w toku, czeka na DNS Hostinger)
- ⏳ Maile transakcyjne z domeny rentspot.eu (czeka na DNS)
- 🔴 **Reguły otwarcia (godziny pracy + dni wolne)** — blokuje rezerwacje poza godzinami. Krytyczne dla zaufania partnera.
- 🔴 **Reguły cen sezonowych** — bez tego nie sprzedamy do narciarskich/letnich. Wysoki priorytet.
- 🟡 **Faktury (integracja Fakturownia albo prosty PDF VAT)** — drugi blocker po godzinach pracy.

### Q3 2026 (post-launch, pierwsi partnerzy)
- 🟡 **Lista rezerwowa (waitlist)** — kiedy popularny sprzęt jest zajęty
- 🟡 **Kody rabatowe** — standard sprzedażowy
- 🟡 **Karnety / vouchery** — narciarskie 7-dni, SUP 5-wynajmów
- 🟡 **Synchronizacja Google Calendar (1-way push)** — partner widzi swoje wynajmy w kalendarzu
- 🟡 **Wtyczka WordPress** — wstawiany formularz rezerwacji (iframe albo embed)

### Q4 2026 (skalowanie)
- 🟢 **Prowizje pracowników** — dla większych wypożyczalni
- 🟢 **Multi-lokalizacja per-tenant** (jeden właściciel, 3 lokalizacje)
- 🟢 **Multi-language UX** (PL/EN/DE/UA — turyści w górach i nad morzem)
- 🟢 **Marketplace API** (partnerzy techniczni → integracje własne)

### Cały czas (continuous):
- AI asystent — co miesiąc 1-2 nowe write tools
- Trust scoring — dostrajanie scoringu na podstawie realnych incydentów
- UX panelu — feedback od pierwszych partnerów

---

## 9. Wnioski

1. **Bookero NIE jest naszym bezpośrednim konkurentem w ścisłym sensie.** Bookero to silnik rezerwacji uniwersalny; my jesteśmy wertykalem dla wypożyczalni sportowej. Współistnienie możliwe — wypożyczalnia może mieć Bookero do "pakietu lekcji nauki snowboardingu" (rezerwacja usługi) i RentSpot do wypożyczenia desek (rezerwacja sprzętu z kaucją + umową).

2. **Lepiej pozycjonować RentSpot jako "Bookero+" dla wypożyczalni**, nie jako "alternatywa Bookero". Komunikat: "Jeśli masz wypożyczalnię — Bookero rozwiązuje 30% Twoich problemów. RentSpot rozwiązuje 100%."

3. **Krytyczne luki funkcyjne do zamknięcia w Q2:** godziny pracy, ceny sezonowe, faktury. Bez tych trzech wypożyczalnie 30+ wynajmów/mc nas odrzucą.

4. **Nasza realna przewaga technologiczna** to AI + trust scoring + multi-tenant marketplace. Tego Bookero nie ma i prawdopodobnie nie będzie miał (to wymaga przebudowy ich silnika).

5. **Cena ~40-100 PLN/mc** to dobry sweet spot. Bookero ustawia poprzeczkę "tanie z brakami albo drogie z dodatkami", my możemy być "uczciwie wyceniony pakiet wszystkiego dla wypożyczalni".

6. **Battlecard sprzedażowa** powinna być przygotowana przed kontaktami z 1330 wypożyczalniami z bazy. Najczęstsze obiekcje już przewidziane wyżej.

---

*Analiza: 2026-05-07. Źródła: bookero.pl (publiczna strona + cennik + funkcje), kod RentSpot branch `main` commit `a6e1b77`.*
