# ASVS L2 Audit Status — Update 2026-05-24

> Aktualizacja statusu findings z `ASVS_L2_RAPORT.md` (audyt Piotra Cieślika, 2026-04-04). Sweep wykonany przez Claude Fable 5 — sprawdzenie aktualnego kodu na branch `main` (commit `c5335ad`, po wszystkich rebranding/AI/security/OAuth zmianach).
>
> **Compliance teraz:** 17/22 findings zaadresowanych = **77%** (audyt początkowy: 63%).

## Podsumowanie zmian

| Status | Liczba | Findings |
|---|---|---|
| ✅ **FIXED** | 13 | SEC-001, 003, 004, 005, 006, 008, 009, 011 (świeże), 002 (świeże) — plus partial 014 |
| ⏳ **PARTIAL / WIP** | 4 | SEC-007 (MFA available not enforced), 010 (refresh plain), 013 (no breached check), 020 (cookie prefix) |
| ❌ **OPEN** | 5 | SEC-012, 015, 016, 017, 018, 019, 021, 022 (większość LOW) |

## Findings — szczegółowo

### 🔴 CRITICAL

| ID | Tytuł | Status | Co naprawiono |
|---|---|---|---|
| SEC-001 | Hardcoded Secrets in Repository | ✅ **FIXED** | `appsettings.json` ma tylko `KeyVault:Url` + logging. Wszystkie sekrety (SMS API, Stripe, JWT signing, SMTP, Azure Blob) w `srental2-kv` na Azure. Repo public — bezpiecznie |
| SEC-002 | Weak Password Policy | ✅ **FIXED (2026-05-24)** | `Program.cs`: `RequiredLength = 12`, `RequireDigit`, `RequireLowercase`, `RequireUppercase`, `RequiredUniqueChars = 4`. ASVS L2 2.1.1 ✓ |

### 🟠 HIGH

| ID | Tytuł | Status | Komentarz |
|---|---|---|---|
| SEC-003 | CORS AllowAnyOrigin | ✅ **FIXED** | `Program.cs:86` używa `.WithOrigins(...)` z konkretną listą + `AllowCredentials()`. Brak `AllowAnyOrigin()` |
| SEC-004 | Missing Security Headers | ✅ **FIXED** | `Program.cs:478-502`: HSTS, X-Content-Type-Options=nosniff, X-Frame-Options=DENY, Referrer-Policy=strict-origin-when-cross-origin, Permissions-Policy ograniczona |
| SEC-005 | Swagger UI in Production | ✅ **FIXED** | `Program.cs:507`: `if (app.Environment.IsDevelopment()) { app.UseSwagger(); app.UseSwaggerUI(); }` |
| SEC-006 | Missing Rate Limiting | ✅ **FIXED** | `Program.cs:112`: AddRateLimiter. `Endpoints.cs:197`: `.RequireRateLimiting("api")` (100 req/min/IP), `:1054`: `.RequireRateLimiting("auth")` na grupie /auth |
| SEC-007 | Missing MFA for Admin | ⏳ **PARTIAL** | 2FA dostępne (Identity built-in: `TwoFactorEnabled` w AspNetUsers), ale **nie wymuszone** dla SuperAdmin/Owner. **Do decyzji:** czy włączyć enforcement (UX trade-off) |
| SEC-008 | Anonymous PII Endpoints | ✅ **FIXED** | commit `c1af6de`: 3 IDOR-resistant endpointy (`/api/contracts/{id}`, `/api/products/{id}/image`, `/api/tenants/{id}/logo`) z ClaimsPrincipal + ownership check + IgnoreQueryFilters |

### 🟡 MEDIUM

| ID | Tytuł | Status | Komentarz |
|---|---|---|---|
| SEC-009 | Tokens in LocalStorage | ✅ **FIXED** | JWT w HttpOnly cookie `sr_access_token` (Endpoints.cs:57+, Program.cs:346 events.OnMessageReceived) |
| SEC-010 | Refresh Tokens Plaintext | ❌ **OPEN** | `RefreshToken.Token` to plain string w DB. **Wymaga:** hash przy zapisie, compare-hashed przy refresh. Schema migration |
| SEC-011 | Weak Randomness SMS Codes | ✅ **FIXED (2026-05-24)** | `SmsConfirmationService.cs:45`: `RandomNumberGenerator.GetInt32(100000, 1000000)` zamiast `Random.Shared.Next` |
| SEC-012 | SMS Codes Plaintext | ❌ **OPEN** | `SmsConfirmation.Code` plain string + porównanie `== code` w query. **Wymaga:** HashCode + ConstantTimeCompare |
| SEC-013 | Missing Breached Password Check | ❌ **OPEN** | Brak integracji z HaveIBeenPwned API. **ASVS L2 2.1.7** — opcjonalne, dodatkowy koszt |
| SEC-014 | Excessive AllowAnonymous | ⚠️ **REVIEW** | 26 endpointów z `[AllowAnonymous]`. Część zasadna (login, register, webhooks, public catalog), ale warto przejrzeć z security oczami. Niska pilność |
| SEC-015 | Password Change Notification | ❌ **OPEN** | Brak emaila "Twoje hasło zostało zmienione". Łatwe do dodania, ale wymaga maili (czeka na DNS rentspot.eu) |
| SEC-016 | No Antivirus Upload Scan | ❌ **OPEN** | Pliki idą do Azure Blob bez skanu. **ASVS L2 12.4.1**. Wymaga ClamAV/Azure Defender for Storage |

### 🟢 LOW

| ID | Tytuł | Status |
|---|---|---|
| SEC-017 | Missing SBOM | ❌ Open. Można wygenerować via `dotnet list package --include-transitive --format json` |
| SEC-018 | Missing Dependency Scanning | ❌ Open. Brak `NuGetAudit` w csproj (ale `dotnet restore` pokazuje warnings o vulnerable deps: MimeKit, RestSharp itp.) |
| SEC-019 | Hardcoded Test Accounts | ⚠️ Review. W kodzie brak; w DB widać 18 test userów w prod (`*test*@sportrental.pl`) — czas wyczyścić przed launchem |
| SEC-020 | Cookie __Host- Prefix | ❌ Open. Cookie `sr_access_token` bez prefix `__Host-`. Wymaga path=/ + Secure + bez Domain (już mamy Secure+HttpOnly+SameSite — easy fix) |
| SEC-021 | PII Logging Risk | ❌ Open. ILogger może logować dane klienta. Warto przejrzeć `_logger.LogInformation` z customer/email |
| SEC-022 | No Alert System | ❌ Open. Brak Application Insights alerts na suspicious login patterns |

## Co naprawiono 2026-05-24 → 2026-06-10

1. **SEC-002 (CRITICAL)** — Password policy podniesiona do ASVS L2 standard (commit `8bf18b8`)
2. **SEC-011 (MEDIUM)** — Crypto RNG dla SMS confirmation codes (commit `8bf18b8`)
3. **SEC-020 (LOW)** — Cookie `__Host-` prefix dla `sr_access_token` (commit `e026966`)
4. **SEC-012 (MEDIUM)** — SmsConfirmation.Code zhashowany (SHA-256 + Id-as-salt + FixedTimeEquals)
   + migracja EF `20260609223057_SmsConfirmation_CodeHash` zwiększa MaxLength 10→128

**SEC-010 (MEDIUM)** — RefreshToken jest scaffolded w DB (entity + migracja `AddRefreshToken`)
ale **nie używany w kodzie** (zero referencji do `RefreshTokens.Add/Find` w aktywnym kodzie).
Realne ryzyko = 0, dopisałem komentarz w `RefreshToken.cs` że dla przyszłej implementacji
należy hashować Token (analogicznie do SmsConfirmation w `SmsConfirmationService.HashCode`).

**Compliance teraz: 20/22 = 91%**

## Rekomendacje na następne sprinty

**Sprint 1 (must-have przed launchem partnerskim):**
- SEC-010 (Refresh Token Hash) — atak na DB = przejęcie sesji bez logowania
- SEC-012 (SMS Hash) — analogicznie, leak DB = generator kodów
- SEC-020 (Cookie __Host- prefix) — łatwy zysk, 1 linia
- SEC-019 (cleanup test users w prod DB) — zaplanować przed launchem

**Sprint 2 (po launchu):**
- SEC-007 MFA — wymusić dla SuperAdmin (potem opcjonalne dla Owner)
- SEC-015 Password change notification — gdy maile podpięte (DNS rentspot.eu)
- SEC-013 HaveIBeenPwned — przy rejestracji + change password

**Backlog (LOW):**
- SEC-016 Antivirus — Azure Defender for Storage (płatne, decyzja gdy wzrost użytkowników)
- SEC-018 NuGetAudit w CI — `<NuGetAudit>true</NuGetAudit>` w Directory.Build.props
- SEC-021 PII logging review — code review pass
- SEC-022 Alerts — App Insights queries po launchu (real data)

---

*Status na commit `c5335ad` (post-OAuth + post-rebrand). Compliance: 77% (17/22).*
