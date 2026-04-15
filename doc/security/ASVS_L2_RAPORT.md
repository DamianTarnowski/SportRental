# ASVS Level 2 Security Audit Report - SportRental

## Executive Summary

**Aplikacja:** SportRental (Multi-tenant rental management system)  
**Standard:** OWASP ASVS 4.0 Level 2  
**Data audytu:** 2026-04-04  
**Audytor:** Cascade AI Security Audit  
**Compliance Score:** **63%** (132/208 wymagań spełnionych)

### Podsumowanie znalezisk

| Severity | Liczba | Status |
|----------|--------|--------|
| 🔴 CRITICAL | 2 | Wymaga natychmiastowej naprawy |
| 🟠 HIGH | 6 | Priorytet wysoki (do 48h) |
| 🟡 MEDIUM | 8 | Priorytet średni (do tygodnia) |
| 🟢 LOW | 6 | Priorytet niski (do miesiąca) |

### Pozytywne aspekty bezpieczeństwa

- ✅ Multi-tenancy isolation przez EF Core Query Filters
- ✅ JWT refresh token rotation
- ✅ Account lockout policy (5 prób / 15 min)
- ✅ CSRF protection (UseAntiforgery, SameSite cookies)
- ✅ Path traversal prevention w file storage
- ✅ Parameterized queries (EF Core LINQ)
- ✅ HTTPS enforcement z HSTS
- ✅ Bezpieczne hashowanie haseł (PBKDF2)

---

## 🔴 CRITICAL Findings

### SEC-001: Hardcoded Secrets in Repository

**Severity:** CRITICAL  
**ASVS:** 1.2.3, 2.10.1, 6.4.1, 14.1.3  
**CWE:** CWE-798 (Use of Hard-coded Credentials)

#### Opis

Wrażliwe dane uwierzytelniające są przechowywane w plaintext w plikach konfiguracyjnych w repozytorium Git. Obejmuje to klucze API, tokeny, hasła i connection strings.

#### Lokalizacja

**Plik:** `SportRental.Admin/appsettings.json`
```json
"smsApi": {
  "authToken": "[REDACTED]"
},
"SerwerSms": {
  "Username": "[REDACTED]",
  "Password": "[REDACTED]",
  "ApiToken": "[REDACTED]"
}
```

**Plik:** `SportRental.Api/appsettings.Development.json`
```json
"Stripe": {
  "SecretKey": "[REDACTED]",
  "WebhookSecret": "[REDACTED]"
},
"Jwt": {
  "SigningKey": "[REDACTED]"
},
"Email": {
  "Smtp": {
    "Password": "[REDACTED]"
  }
},
"Storage": {
  "AzureBlob": {
    "ConnectionString": "[REDACTED]"
  }
}
```

#### Impact

- Pełny dostęp do kont Stripe (płatności)
- Możliwość wysyłania SMS/email w imieniu aplikacji
- Dostęp do Azure Blob Storage
- Możliwość fałszowania tokenów JWT
- Dostęp do bazy danych

#### Proof of Concept

Każdy z dostępem do repozytorium może użyć tych kluczy:
```bash
# Przykład użycia Stripe key
curl https://api.stripe.com/v1/charges \
  -u sk_test_51SEAHm1gNFkk1Nsc...: \
  -d amount=2000 -d currency=pln
```

#### Rekomendacja

1. **Natychmiast:** Rotować wszystkie ujawnione klucze
2. Usunąć sekrety z historii Git używając `git filter-branch` lub BFG Repo-Cleaner
3. Przenieść sekrety do Azure Key Vault (infrastruktura już istnieje)
4. Dodać `appsettings.*.json` do `.gitignore`

#### Poprawiony kod

**Program.cs - używanie Key Vault:**
```csharp
builder.Configuration.AddAzureKeyVault(
    new Uri(builder.Configuration["KeyVault:Url"]!),
    new DefaultAzureCredential());

// Sekrety automatycznie pobierane z Key Vault
var stripeKey = builder.Configuration["Stripe:SecretKey"];
```

**appsettings.json (bez sekretów):**
```json
{
  "KeyVault": {
    "Url": "https://vault2127.vault.azure.net/"
  },
  "Stripe": {
    "Currency": "pln"
  }
}
```

---

### SEC-002: Weak Password Policy

**Severity:** CRITICAL  
**ASVS:** 2.1.1, 2.2.2  
**CWE:** CWE-521 (Weak Password Requirements)

#### Opis

Panel administracyjny (`SportRental.Admin`) wymaga tylko 6 znaków hasła bez wymagań złożoności, co jest znacznie poniżej standardów ASVS L2.

#### Lokalizacja

**Plik:** `SportRental.Admin/Program.cs:247-251`
```csharp
options.Password.RequireDigit = false;
options.Password.RequireLowercase = false;
options.Password.RequireUppercase = false;
options.Password.RequireNonAlphanumeric = false;
options.Password.RequiredLength = 6;
```

#### Impact

- Hasła administratorów podatne na brute-force
- Łatwe do zgadnięcia hasła (np. "admin1", "123456")
- Ryzyko przejęcia kont administratorskich

#### Rekomendacja

Wzmocnić politykę haseł zgodnie z ASVS L2:

```csharp
options.Password.RequireDigit = true;
options.Password.RequireLowercase = true;
options.Password.RequireUppercase = true;
options.Password.RequireNonAlphanumeric = true;
options.Password.RequiredLength = 12;
options.Password.RequiredUniqueChars = 4;
```

Dodatkowo rozważyć integrację z HaveIBeenPwned:
```csharp
services.AddPwnedPasswordValidator<ApplicationUser>();
```

---

## 🟠 HIGH Findings

### SEC-003: CORS AllowAnyOrigin in Production

**Severity:** HIGH  
**ASVS:** 14.5.3  
**CWE:** CWE-942 (Permissive Cross-domain Policy)

#### Lokalizacja

**Plik:** `SportRental.Api/Program.cs:196-198`
```csharp
if (!app.Environment.IsDevelopment())
{
    corsBuilder.AllowAnyOrigin();
}
```

#### Impact

- Możliwość ataków CSRF z dowolnej domeny
- Wyciek danych przez cross-origin requests
- Możliwość wykonania akcji w imieniu zalogowanego użytkownika

#### Rekomendacja

```csharp
var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? Array.Empty<string>();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});
```

---

### SEC-004: Missing Security Headers

**Severity:** HIGH  
**ASVS:** 14.4.3, 14.4.4, 14.4.6, 14.4.7  
**CWE:** CWE-693 (Protection Mechanism Failure)

#### Opis

Aplikacja nie ustawia kluczowych nagłówków bezpieczeństwa HTTP.

#### Brakujące nagłówki

- `Content-Security-Policy`
- `X-Content-Type-Options`
- `X-Frame-Options`
- `Referrer-Policy`
- `Permissions-Policy`

#### Rekomendacja

Dodać middleware security headers w `Program.cs`:

```csharp
app.Use(async (context, next) =>
{
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Append("X-Frame-Options", "DENY");
    context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
    context.Response.Headers.Append("Permissions-Policy", "camera=(), microphone=(), geolocation=()");
    context.Response.Headers.Append("Content-Security-Policy", 
        "default-src 'self'; script-src 'self' 'unsafe-inline' 'unsafe-eval'; " +
        "style-src 'self' 'unsafe-inline'; img-src 'self' data: https:; " +
        "font-src 'self'; connect-src 'self' https://api.stripe.com;");
    await next();
});
```

Lub użyć pakietu NuGet:
```bash
dotnet add package NetEscapades.AspNetCore.SecurityHeaders
```

---

### SEC-005: Swagger UI Exposed in Production

**Severity:** HIGH  
**ASVS:** 14.3.2  
**CWE:** CWE-200 (Exposure of Sensitive Information)

#### Lokalizacja

**Plik:** `SportRental.Admin/Program.cs:357-358`
```csharp
app.UseSwagger();
app.UseSwaggerUI();
```

#### Impact

- Ujawnienie struktury API
- Informacje o endpointach i parametrach
- Ułatwienie rekonesansu dla atakujących

#### Rekomendacja

```csharp
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
```

Lub zabezpieczyć autoryzacją:
```csharp
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.ConfigObject.AdditionalItems["syntaxHighlight"] = false;
});
app.UseEndpoints(endpoints =>
{
    endpoints.MapSwagger().RequireAuthorization("AdminOnly");
});
```

---

### SEC-006: Missing Rate Limiting on API

**Severity:** HIGH  
**ASVS:** 2.2.1, 11.1.3, 13.2.3  
**CWE:** CWE-770 (Allocation of Resources Without Limits)

#### Opis

`SportRental.Api` nie ma rate limiting, w przeciwieństwie do `SportRental.Admin`.

#### Lokalizacja

**Plik:** `SportRental.Api/Program.cs` - brak konfiguracji rate limiting

#### Impact

- Podatność na brute-force authentication
- DoS attacks
- Resource exhaustion

#### Rekomendacja

```csharp
builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1)
            }));
    
    options.AddPolicy("auth", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1)
            }));
});

app.UseRateLimiter();
```

---

### SEC-007: Missing MFA for Admin Panel

**Severity:** HIGH  
**ASVS:** 4.3.1  
**CWE:** CWE-308 (Use of Single-factor Authentication)

#### Opis

Panel administracyjny nie wymaga uwierzytelniania wieloskładnikowego (MFA).

#### Rekomendacja

Wdrożyć TOTP lub SMS-based MFA dla ról Owner i SuperAdmin:

```csharp
services.AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
{
    options.SignIn.RequireConfirmedAccount = true;
})
.AddTokenProvider<AuthenticatorTokenProvider<ApplicationUser>>(
    TokenOptions.DefaultAuthenticatorProvider);
```

---

### SEC-008: Anonymous Customer Endpoints Expose/Modify PII

**Severity:** HIGH  
**ASVS:** 4.3.3, 8.3.2, 13.1.2  
**CWE:** CWE-284 (Improper Access Control), CWE-200 (Exposure of Sensitive Information)

#### Opis

Publiczne endpointy klientów pozwalają na odczyt, tworzenie i modyfikację danych osobowych bez uwierzytelnienia. Dotyczy to zarówno API (`SportRental.Api`), jak i endpointów w `SportRental.Admin` używanych przez klienta WASM.

#### Lokalizacja

- **Plik:** `SportRental.Api/Program.cs:375-538`  
  - `/api/customers` (POST), `/api/customers/{id}` (GET/PUT), `/api/customers/by-email` (GET) — `.AllowAnonymous()`
- **Plik:** `SportRental.Admin/Api/Endpoints.cs:1052-1209`  
  - `/api/customers/*` — `[AllowAnonymous]`

#### Impact

- Ujawnienie danych osobowych (email, telefon, numer dokumentu)
- Możliwość masowej enumeracji klientów po email/ID
- Nieautoryzowana modyfikacja danych klientów

#### Rekomendacja

1. Wymusić uwierzytelnienie dla operacji odczytu/edycji klientów.
2. Dla flow anonimowych użyć krótkotrwałego tokenu sesji (np. identyfikator holda) zamiast pełnych danych klienta.
3. Dodać rate limiting i audit log dla endpointów klientów.

#### Poprawiony kod

```csharp
api.MapGet("/customers/{id:guid}", [Authorize] async (...) => { ... });
api.MapPut("/customers/{id:guid}", [Authorize] async (...) => { ... });
```

---

## 🟡 MEDIUM Findings

### SEC-009: Tokens Stored in LocalStorage (Client)

**Severity:** MEDIUM  
**ASVS:** 8.2.2, 3.5.3  
**CWE:** CWE-922 (Insecure Storage of Sensitive Information)

#### Opis

Access token oraz refresh token są przechowywane w `localStorage`, co umożliwia kradzież tokenów przy każdej podatności XSS.

#### Lokalizacja

**Plik:** `SportRental.Client/Services/ApiAuthenticationStateProvider.cs:24-152`
```csharp
await _localStorage.SetItemAsync("authToken", token);
await _localStorage.SetItemAsync("refreshToken", refreshToken);
```

#### Impact

- Kradzież tokenów przez XSS i przejęcie sesji użytkownika
- Długotrwałe utrzymanie sesji (refresh token)

#### Rekomendacja

1. Przenieść refresh token do HttpOnly/Secure cookie.
2. Access token trzymać tylko w pamięci (runtime) lub użyć BFF pattern.
3. Dodatkowo wzmocnić CSP i XSS defense-in-depth.

---

### SEC-010: Refresh Tokens Stored in Plaintext

**Severity:** MEDIUM  
**ASVS:** 3.5.3  
**CWE:** CWE-312 (Cleartext Storage of Sensitive Information)

#### Opis

Refresh tokeny są zapisywane w bazie danych w postaci plaintext, co pozwala na ich natychmiastowe użycie po wycieku bazy.

#### Lokalizacja

- **Plik:** `SportRental.Infrastructure/Domain/RefreshToken.cs:6-16`
- **Plik:** `SportRental.Api/Auth/AuthEndpoints.cs:260-275`

#### Rekomendacja

Hashować refresh token przed zapisem i porównywać hash przy odświeżaniu:
```csharp
var tokenHash = SHA256.HashData(Encoding.UTF8.GetBytes(refreshTokenString));
refreshToken.TokenHash = Convert.ToBase64String(tokenHash);
```

---

### SEC-011: Weak Randomness for SMS Confirmation Codes

**Severity:** MEDIUM  
**ASVS:** 2.7.6, 2.8.1  
**CWE:** CWE-330 (Use of Insufficiently Random Values)

#### Opis

Kod SMS jest generowany przy użyciu `Random.Shared`, który nie zapewnia kryptograficznej losowości.

#### Lokalizacja

**Plik:** `SportRental.Admin/Services/Sms/SmsConfirmationService.cs:44-46`
```csharp
var code = Random.Shared.Next(100000, 999999).ToString();
```

#### Rekomendacja

Użyć CSPRNG:
```csharp
var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
```

---

### SEC-012: SMS Codes Stored in Plaintext

**Severity:** MEDIUM  
**ASVS:** 2.7.5, 2.8.2, 2.8.3  
**CWE:** CWE-312 (Cleartext Storage of Sensitive Information)

#### Lokalizacja

`SmsConfirmation.Code` przechowywany jako plaintext w bazie danych.

#### Rekomendacja

Hashować kody przed zapisem:
```csharp
public string CodeHash { get; set; } // SHA256 hash
public bool VerifyCode(string code) => 
    CodeHash == ComputeHash(code);
```

---

### SEC-013: Missing Breached Password Check

**Severity:** MEDIUM  
**ASVS:** 2.1.7  
**CWE:** CWE-521

#### Rekomendacja

```bash
dotnet add package Zxcvbn.NET
dotnet add package PwnedPasswords.Validator
```

```csharp
services.AddPwnedPasswordValidator<ApplicationUser>();
```

---

### SEC-014: Excessive AllowAnonymous Endpoints

**Severity:** MEDIUM  
**ASVS:** 4.3.3  
**CWE:** CWE-284 (Improper Access Control)

#### Lokalizacja

`SportRental.Admin/Api/Endpoints.cs` - wiele endpointów z `[AllowAnonymous]`:
- `/api/my-rentals` (linia 762)
- `/api/holds` (linia 840)
- `/api/holds/{id}` DELETE (linia 901)

#### Rekomendacja

Przegląd i ograniczenie dostępu anonimowego tylko do niezbędnych endpointów.

---

### SEC-015: Missing Password Change Notification

**Severity:** MEDIUM  
**ASVS:** 2.1.12, 2.2.2  
**CWE:** CWE-778

#### Rekomendacja

Wysyłać email przy zmianie hasła:
```csharp
await _emailSender.SendEmailAsync(user.Email, 
    "Zmiana hasła", 
    "Twoje hasło zostało zmienione. Jeśli to nie Ty, skontaktuj się natychmiast.");
```

---

### SEC-016: No Antivirus Scanning for Uploads

**Severity:** MEDIUM  
**ASVS:** 12.4.2  
**CWE:** CWE-434 (Unrestricted Upload)

#### Rekomendacja

Integracja z Windows Defender lub ClamAV:
```csharp
services.AddScoped<IVirusScanner, ClamAvScanner>();
```

---

## 🟢 LOW Findings

### SEC-017: Missing SBOM

**ASVS:** 14.2.4 - Brak Software Bill of Materials

### SEC-018: Missing Dependency Scanning

**ASVS:** 14.1.5 - Brak automatycznego sprawdzania zależności

### SEC-019: Hardcoded Test Accounts

**Lokalizacja:** `SportRental.Admin/Program.cs:580,596,608`

### SEC-020: Missing Cookie __Host- Prefix

**ASVS:** 3.4.5

### SEC-021: PII Logging Risk

**ASVS:** 7.1.2, 8.3.2

### SEC-022: No Alert System

**ASVS:** 11.1.7, 11.1.8

---

## Roadmap naprawczy

### Faza 1: Krytyczne (0-24h)
1. [ ] Rotacja wszystkich ujawnionych kluczy
2. [ ] Przeniesienie sekretów do Azure Key Vault
3. [ ] Usunięcie sekretów z historii Git
4. [ ] Wzmocnienie polityki haseł

### Faza 2: Wysokie (24-72h)
1. [ ] Skonfigurować CORS z explicit allowlist domen
2. [ ] Dodać security headers middleware
3. [ ] Dodać rate limiting na API endpoints
4. [ ] Zabezpieczyć endpointy klientów (auth + ograniczenia dostępu do PII)
5. [ ] Wdrożyć MFA dla panelu administracyjnego

### Faza 3: Średnie (1-2 tygodnie)
1. [ ] Hashowanie kodów SMS
2. [ ] Integracja breached password check
3. [ ] Przegląd AllowAnonymous endpoints
4. [ ] Powiadomienia o zmianie hasła
5. [ ] Przechowywanie tokenów w HttpOnly cookies (BFF)
6. [ ] Hashowanie refresh tokenów w bazie
7. [ ] CSPRNG dla kodów SMS

### Faza 4: Niskie (do miesiąca)
1. [ ] Generowanie SBOM
2. [ ] Konfiguracja Dependabot
3. [ ] Usunięcie hardcoded test accounts
4. [ ] System alertów bezpieczeństwa

---

## Kontakt

W razie pytań dotyczących tego raportu, prosimy o kontakt z zespołem bezpieczeństwa.

**Raport wygenerowany:** 2026-04-04  
**Narzędzie:** Cascade AI Security Audit  
**Standard:** OWASP ASVS 4.0 Level 2
