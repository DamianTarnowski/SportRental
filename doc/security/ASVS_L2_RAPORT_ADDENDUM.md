# ASVS L2 Audit — Addendum do raportu z 2026-04-04

## Cel tego dokumentu

Raport `ASVS_L2_RAPORT.md` (Cascade AI, 2026-04-04) wykrył 22 zagadnienia,
ale przegląd manualny ujawnił:

1. **5 krytycznych podatności** pominiętych w raporcie.
2. **2 pozycje z listy "pozytywne aspekty"** są nieprawdziwe.
3. **Kilka dodatkowych uzupełnień** do istniejących findings.

Addendum należy czytać **obok** raportu głównego, nie zamiast.

---

## Korekta "pozytywnych aspektów" z raportu głównego

Raport (linie 22, 26) wymienia jako zalety:

- ❌ ~~Multi-tenancy isolation przez EF Core Query Filters~~
- ❌ ~~Path traversal prevention w file storage~~

**Rzeczywistość:** oba są częściowe / dziurawe — szczegóły w SEC-A03
i SEC-A05 poniżej. Proponuję usunąć te pozycje z listy pozytywów albo
zastąpić je: *"EF Core query filters skonfigurowane (ale obchodzone
w kilku endpointach — zob. SEC-A03)"*.

---

## Pominięte findings (CRITICAL / HIGH)

### SEC-A01: Superadmin password reset on every startup (production)

**Severity:** CRITICAL
**ASVS:** 2.1.1, 2.2.2, 8.2.2
**CWE:** CWE-798 (Hard-coded Credentials), CWE-259 (Hard-coded Password)

#### Lokalizacja

`SportRental.Admin/Program.cs:556-604` — blok seedowania superadmina.

#### Opis

Na każdym starcie aplikacji hasło użytkownika `hdtdtr@gmail.com` było
resetowane do zahardkodowanej wartości. **STATUS: naprawione** — reset/seed
jest teraz ograniczony do `IsDevelopment()` i czyta hasło z konfiguracji
(`Admin:DevPassword`, user-secrets/env), a nie z literału w kodzie.

#### Impact

- Każdy deploy nadpisuje hasło superadmina wartością z configu.
- Jeśli admin zmieni swoje hasło, kolejny restart aplikacji cofa tę zmianę.
- Hasło jest znane wszystkim, którzy widzieli `appsettings.json` w historii
  Git lub Key Vault — skutecznie: hardcoded admin password.
- ASVS L2 wymaga, aby konta administracyjne nie miały haseł ustawianych
  przez proces wdrożeniowy (2.2.2).

#### Rekomendacja

Seedować hasło **tylko raz** (gdy użytkownik nie istnieje) i tylko
w środowisku deweloperskim; w produkcji superadmin musi ustawić hasło
ręcznie lub przez flow "zapomniałem hasła".

```csharp
if (app.Environment.IsDevelopment())
{
    var user = await userManager.FindByEmailAsync(superAdminEmail);
    if (user is null)
    {
        user = new ApplicationUser { ... };
        await userManager.CreateAsync(user, initialPassword);
        await userManager.AddToRoleAsync(user, "SuperAdmin");
    }
    // brak ResetPasswordAsync — tylko pierwsze utworzenie
}
```

---

### SEC-A02: Stripe webhook accepts unsigned events when secret is missing

**Severity:** CRITICAL
**ASVS:** 9.2.3, 11.1.1, 11.1.4
**CWE:** CWE-345 (Insufficient Verification of Data Authenticity)

#### Lokalizacja

`SportRental.Api/Payments/StripeWebhookEndpoints.cs:54-62`

#### Opis

Kod próbuje zweryfikować sygnaturę webhooka Stripe używając
`EventUtility.ConstructEvent(json, signature, webhookSecret)`, **ale**
gdy `webhookSecret` jest pusty/null, wpada w fallback:

```csharp
if (string.IsNullOrWhiteSpace(webhookSecret))
{
    stripeEvent = EventUtility.ParseEvent(json);   // ⚠️ BEZ weryfikacji
}
```

#### Impact

Atakujący może wysłać POST na `/stripe/webhook` z dowolnym payloadem
typu `checkout.session.completed` → aplikacja oznaczy wynajem jako
opłacony bez faktycznej płatności. **Bezpośredni wpływ na finanse.**

Ryzyko realne, jeśli Key Vault chwilowo nie odda sekretu lub ktoś
przez pomyłkę usunie `Stripe:WebhookSecret`.

#### Rekomendacja

Usunąć fallback. Brak `webhookSecret` musi powodować **5xx** lub
odmowę przyjęcia webhooka, nigdy akceptację bez podpisu.

```csharp
if (string.IsNullOrWhiteSpace(webhookSecret))
{
    logger.LogError("Stripe webhook secret is not configured");
    return Results.Problem("Webhook verification is not configured", statusCode: 500);
}
stripeEvent = EventUtility.ConstructEvent(json, signature, webhookSecret);
```

---

### SEC-A03: Anonymous endpoints bypass multi-tenant isolation

**Severity:** CRITICAL
**ASVS:** 4.1.3, 4.2.1, 8.3.1
**CWE:** CWE-639 (Authorization Bypass Through User-Controlled Key),
CWE-284 (Improper Access Control)

#### Lokalizacja

- `SportRental.Admin/Api/Endpoints.cs:762` — `GET /api/my-rentals [AllowAnonymous]` + `IgnoreQueryFilters()`
- `SportRental.Admin/Api/Endpoints.cs:840` — `GET /api/holds [AllowAnonymous]`
- `SportRental.Admin/Api/Endpoints.cs:1053` — `GET /customers/by-email [AllowAnonymous]`
- `SportRental.Admin/Api/Endpoints.cs:1105` — `POST /customers [AllowAnonymous]`
- `SportRental.Admin/Api/Endpoints.cs:1157` — `PUT /customers [AllowAnonymous]`
- `SportRental.Api/Program.cs:GetTenantId()` — zwraca `Guid.Empty` dla
  anonimowego wywołania; downstream interpretowane jako "brak filtra" =
  wszystkie tenanty.

#### Opis

EF Core query filters rzeczywiście filtrują po `TenantId`, ale:

1. Endpointy `/my-rentals` i `/holds` używają `.IgnoreQueryFilters()`
   — wyłączają filtr całkowicie.
2. `GetTenantId()` dla anonimowego requestu zwraca `Guid.Empty`;
   gdy endpoint filtruje po `x.TenantId == tenantId`, dopasowanie
   trafia na 0 rekordów **albo** — jeśli ktoś ma `TenantId == Guid.Empty`
   w bazie (legacy) — wszystkie takie rekordy.
3. PUT /customers ma bug: sprawdzenie `customer.TenantId == requesterTenantId`
   przepuszcza, gdy `requesterTenantId == Guid.Empty`, bo porównanie
   `Guid.Empty == Guid.Empty` jest `true`.

#### Impact

- Anonimowy GET `/api/my-rentals` zwraca wynajmy **wszystkich wypożyczalni**.
- Anonimowy GET `/customers/by-email?email=x@y.z` enumeruje klientów
  ze wszystkich tenantów — poważne naruszenie RODO.
- Anonimowy POST/PUT `/customers` pozwala modyfikować dane klientów
  dowolnej wypożyczalni.

#### Rekomendacja

1. Usunąć `[AllowAnonymous]` z tych endpointów. Jeśli endpoint ma być
   dostępny dla klientów-gości (np. portal WASM), wymagać tokenu
   jednorazowego związanego z konkretnym `TenantId` + `CustomerId`.
2. Usunąć `.IgnoreQueryFilters()` — jeśli filtr przeszkadza, to jest
   błąd projektu, nie powód żeby filtr wyłączyć.
3. `GetTenantId()` musi rzucać `UnauthorizedAccessException` dla
   anonimowego, nigdy nie zwracać `Guid.Empty`.

---

### SEC-A04: Azure Blob container configured with public blob access

**Severity:** CRITICAL
**ASVS:** 8.3.4, 12.1.1
**CWE:** CWE-200 (Exposure of Sensitive Information), CWE-284

#### Lokalizacja

`SportRental.Admin/Services/Storage/AzureBlobStorage.cs:49`

#### Opis

Inicjalizacja kontenera wywołuje:

```csharp
await container.CreateIfNotExistsAsync(PublicAccessType.Blob);
```

`PublicAccessType.Blob` oznacza, że **każdy obiekt** w tym kontenerze
jest dostępny publicznie przez URL (anonimowy GET), bez SAS token
i bez autoryzacji.

#### Impact

- Umowy najmu (PDF) generowane przez `QuestPdfContractGenerator` i zapisywane
  w ścieżce `contracts/{tenantId}/{rentalId}.pdf` są **publicznie dostępne**
  dla każdego, kto zgadnie / wyciągnie URL z logów / caches / e-maila.
- Umowa zawiera PII (imię, nazwisko, adres, PESEL/dowód osobisty),
  co jest bezpośrednim naruszeniem RODO art. 32.

#### Proof of Concept

```bash
curl https://<storage>.blob.core.windows.net/<container>/contracts/<tenant-guid>/<rental-guid>.pdf
# → 200 OK, PDF z PII
```

#### Rekomendacja

1. Zmienić na `PublicAccessType.None` (pełna prywatność kontenera).
2. Dostęp do plików wydawać przez **time-limited SAS tokens**
   (`BlobSasBuilder`, np. 10 min TTL) generowane per-request po autoryzacji
   użytkownika i sprawdzeniu przynależności pliku do jego tenanta.
3. Rozważyć migrację istniejących obiektów do nowego kontenera
   i unieważnienie starych URLi.

---

### SEC-A05: Path traversal not blocked in blob storage path normalization

**Severity:** HIGH
**ASVS:** 12.3.1, 12.3.2, 12.3.3
**CWE:** CWE-22 (Path Traversal)

#### Lokalizacja

`SportRental.Admin/Services/Storage/AzureBlobStorage.cs:143-155` —
`NormalizeRelativePath`.

#### Opis

Funkcja normalizacji zamienia backslashe na slashe i usuwa wiodący `/`,
ale **nie filtruje** segmentów `..`. W kombinacji z innymi operacjami
(upload/delete) pozwala na manipulację ścieżką:

```csharp
// Przykład: "foo/../../bar.pdf" → po normalizacji nadal zawiera ".."
```

#### Impact

- Zależnie od context-u wywołania: nadpisanie obiektów innego tenanta,
  odczytanie obiektów spoza wyznaczonego prefiksu.
- W połączeniu z SEC-A04 (publiczny kontener) — eskalacja do leak.

#### Rekomendacja

```csharp
private static string NormalizeRelativePath(string path)
{
    if (string.IsNullOrWhiteSpace(path))
        throw new ArgumentException("Path is empty");

    var normalized = path.Replace('\\', '/').TrimStart('/');

    if (normalized.Split('/').Any(segment => segment == ".." || segment == "."))
        throw new ArgumentException("Path traversal segments are not allowed");

    return normalized;
}
```

---

### SEC-A06: Stored XSS in customer confirmation view

**Severity:** HIGH
**ASVS:** 5.3.3, 5.3.4
**CWE:** CWE-79 (Improper Neutralization of Input During Web Page Generation)

#### Lokalizacja

`SportRental.Admin/Api/ConfirmationEndpoints.cs:63-67` — budowa HTML-a
strony potwierdzenia wynajmu.

#### Opis

Pola modelu (m.in. `item.ProductName`, dane klienta) są interpolowane
do szablonu HTML **bez** `HtmlEncoder.Default.Encode(...)`. Nazwa produktu
i notatki do wynajmu są edytowalne przez pracownika wypożyczalni
— atakujący pracownik może wstrzyknąć `<script>` który wykona się
w przeglądarce klienta otwierającego link z SMS-a.

#### Rekomendacja

1. Użyć `Razor` do renderowania strony confirmation (automatyczny escape),
   albo
2. Wszystkie pola interpolowane ręcznie owijać w
   `HtmlEncoder.Default.Encode(value)`.

Dodatkowo: skonfigurować nagłówek `Content-Security-Policy`
(patrz SEC-004 raportu głównego).

---

## Uzupełnienia do istniejących findings

### Do SEC-001 (Hardcoded Secrets)

- Metoda `git filter-branch` wymieniona w raporcie jest deprecated —
  używać **`git filter-repo`** lub **BFG Repo-Cleaner**. `filter-branch`
  jest wolne i łatwo nim uszkodzić repo.
- Plik `SportRental.Admin/appsettings.Development.json` i `SportRental.Api/appsettings.Development.json`
  (w wersji która była w historii) zawierał production connection string
  do Azure PostgreSQL. Sekret jest w historii — rotacja bazy wymagana.

### Do SEC-003 (CORS)

- W `SportRental.Admin/Program.cs:73-90` lista origins zawiera stare
  hosty (np. poprzednie deploymenty Azure Static Web Apps). Przegląd:
  każdy wpis musi odpowiadać aktualnie istniejącej domenie.
- `.AllowCredentials()` w kombinacji z wieloma origins wymaga explicit
  whitelisty — nigdy `AllowAnyOrigin()`. Jest OK, ale zależy od odchudzenia listy.

### Do SEC-006 (Rate Limiting)

- W `SportRental.Admin/Program.cs:104-118` polityka `"api"` **jest zdefiniowana**,
  ale **nie jest attachowana** do żadnego endpointu (brak
  `.RequireRateLimiting("api")`). Raport słusznie to zidentyfikował,
  ale nie odnotował, że infrastruktura już jest — trzeba ją tylko podpiąć.

### Do SEC-010 (Plaintext refresh tokens)

- Dodatkowo: brak **detekcji re-use** zużytego tokena. Obecny kod
  rotuje token przy każdym odświeżeniu, ale jeśli atakujący przechwyci
  token, zużyje go pierwszy — legit user dostaje nowy token i też
  zadziała. W standardzie OAuth2 refresh token reuse powinien
  unieważnić całą rodzinę tokenów (family) i wymusić re-login.

---

## Nowa tabela znalezisk (addendum)

| ID | Severity | Tytuł |
|----|----------|-------|
| SEC-A01 | CRITICAL | Superadmin password reset on every startup |
| SEC-A02 | CRITICAL | Stripe webhook accepts unsigned events |
| SEC-A03 | CRITICAL | Anonymous endpoints bypass tenant isolation |
| SEC-A04 | CRITICAL | Azure Blob container public access |
| SEC-A05 | HIGH | Path traversal in blob path normalization |
| SEC-A06 | HIGH | Stored XSS in confirmation page |

## Poprawiona ocena ryzyka

Raport główny podawał compliance **63%**. Przy doliczeniu SEC-A01..A06
i korekcie fałszywych pozytywów realna ocena jest **niższa** — nie
uruchamiałbym tej aplikacji produkcyjnie z realnymi klientami dopóki
minimum SEC-001, SEC-002, SEC-A01, SEC-A02, SEC-A03, SEC-A04 nie są
poprawione.

---

**Addendum wygenerowany:** 2026-04-15
**Autor:** Claude Opus 4.6 + manualna weryfikacja kodu
**Dotyczy raportu:** `ASVS_L2_RAPORT.md` z 2026-04-04
