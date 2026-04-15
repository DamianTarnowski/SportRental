# ASVS Level 2 Security Audit Checklist - SportRental Application

## Informacje o checkliście

**Aplikacja:** SportRental (Blazor Server + Blazor WASM + ASP.NET Core API)  
**Standard:** OWASP ASVS 4.0 Level 2  
**Data utworzenia:** 2026-04-04  
**Data audytu:** 2026-04-04  
**Audytor:** Cascade AI Security Audit

## Legenda

- ✅ **PASS** - Wymaganie spełnione
- ❌ **FAIL** - Wymaganie niespełnione (luka bezpieczeństwa)
- ⚠️ **PARTIAL** - Częściowo spełnione (wymaga poprawy)
- ⭕ **N/A** - Nie dotyczy tej aplikacji
- 📝 **NOTES** - Dodatkowe uwagi

---

## V1: Architecture, Design and Threat Modeling

### V1.1 Secure Software Development Lifecycle

| ID | Wymaganie | Status | Lokalizacja w kodzie | Uwagi |
|----|-----------|--------|---------------------|-------|
| 1.1.1 | Weryfikacja użycia bezpiecznych komponentów komunikacji między warstwami aplikacji | ✅ | `SportRental.Admin/Program.cs:344`, `SportRental.Api/Program.cs:36` | HTTPS wymuszony, SignalR przez HTTPS, CORS skonfigurowany |
| 1.1.2 | Weryfikacja separacji komponentów o różnych poziomach zaufania | ✅ | Architektura: Admin/API/Client/Infrastructure | Wyraźna separacja warstw, multi-tenancy zaimplementowane |
| 1.1.3 | Weryfikacja że wrażliwe dane nie są logowane | ⚠️ | `SportRental.Admin/Program.cs:675`, wszystkie serwisy | Brak jawnego logowania haseł, ale brak redakcji PII w logach |
| 1.1.4 | Weryfikacja że komponenty są podpisane i weryfikowane | ⚠️ | NuGet packages, `.github/workflows/ci.yml` | NuGet packages z oficjalnych źródeł, brak SBOM |

### V1.2 Authentication Architecture

| ID | Wymaganie | Status | Lokalizacja w kodzie | Uwagi |
|----|-----------|--------|---------------------|-------|
| 1.2.1 | Weryfikacja użycia unikatowych kluczy kryptograficznych per tenant | ❌ | `SportRental.Api/Auth/JwtTokenService.cs:41` | Wspólny klucz JWT dla wszystkich tenantów - ryzyko cross-tenant |
| 1.2.2 | Weryfikacja że mechanizmy autentykacji są odporne na ataki | ✅ | `SportRental.Api/Auth/AuthEndpoints.cs:119`, `Program.cs:252` | Lockout po 5 próbach, 15 min blokady |
| 1.2.3 | Weryfikacja bezpiecznego przechowywania credentials | ❌ | `appsettings.json`, `appsettings.Development.json` | **CRITICAL:** Hardcoded secrets w repo (Stripe, JWT, Azure, SMTP, SMS) |
| 1.2.4 | Weryfikacja że ścieżki autentykacji są jasno zdefiniowane | ✅ | Identity (cookies) vs JWT (API) | Admin=cookies, API=JWT, Client=API via JWT |

### V1.4 Access Control Architecture

| ID | Wymaganie | Status | Lokalizacja w kodzie | Uwagi |
|----|-----------|--------|---------------------|-------|
| 1.4.1 | Weryfikacja egzekwowania kontroli dostępu w zaufanej warstwie | ✅ | API endpoints `[Authorize]`, Blazor Server | Server-side authorization enforcement |
| 1.4.2 | Weryfikacja że nie ma pojedynczego punktu awarii w kontroli dostępu | ✅ | Authorization policies + Query Filters | Defense in depth: role-based + tenant-based |
| 1.4.3 | Weryfikacja izolacji danych między tenantami | ✅ | `ApplicationDbContext.cs:73-150` | Query filters dla wszystkich tenant-scoped entities |
| 1.4.4 | Weryfikacja że kontrola dostępu działa na poziomie rekordu | ✅ | `ApplicationDbContext.cs` HasQueryFilter | Row-level security przez EF Core query filters |

### V1.5 Input and Output Architecture

| ID | Wymaganie | Status | Lokalizacja w kodzie | Uwagi |
|----|-----------|--------|---------------------|-------|
| 1.5.1 | Weryfikacja walidacji inputu na zaufanej warstwie | ✅ | `SportRental.Admin/Api/Endpoints.cs:522-536` | Server-side validation w endpointach API |
| 1.5.2 | Weryfikacja że output encoding jest stosowany | ✅ | Blazor automatic encoding, JSON serialization | Blazor automatycznie enkoduje output |
| 1.5.3 | Weryfikacja że walidacja jest centralna i wielokrotnego użytku | ⚠️ | `SportRental.Shared/Models/` | Częściowo - brak validation attributes na DTOs |
| 1.5.4 | Weryfikacja że encoding jest kontekstowy | ✅ | Blazor/Razor, System.Text.Json | Context-aware encoding przez framework |

### V1.6 Cryptographic Architecture

| ID | Wymaganie | Status | Lokalizacja w kodzie | Uwagi |
|----|-----------|--------|---------------------|-------|
| 1.6.1 | Weryfikacja że klucze kryptograficzne są bezpiecznie zarządzane | ❌ | `appsettings.json` hardcoded keys | **CRITICAL:** Klucze w plaintext w repo, Key Vault skonfigurowany ale nie używany |
| 1.6.2 | Weryfikacja że używane są zatwierdzone algorytmy | ✅ | `JwtTokenService.cs:41` HS256, Identity PBKDF2 | Standardowe algorytmy: HMAC-SHA256 dla JWT, PBKDF2 dla haseł |
| 1.6.3 | Weryfikacja że losowe wartości są kryptograficznie bezpieczne | ✅ | `AuthEndpoints.cs:280-284` RandomNumberGenerator | Kryptograficznie bezpieczny RNG dla refresh tokenów |

---

## V2: Authentication

### V2.1 Password Security

| ID | Wymaganie | Status | Lokalizacja w kodzie | Uwagi |
|----|-----------|--------|---------------------|-------|
| 2.1.1 | Weryfikacja że hasła mają min. 12 znaków (lub 8 z dodatkowymi wymaganiami) | ❌ | `SportRental.Admin/Program.cs:247-251`, `SportRental.Api/Program.cs:57-61` | **HIGH:** Admin=6 znaków (za słabe!), API=8 znaków z complexity |
| 2.1.2 | Weryfikacja że hasła mogą mieć 64+ znaków | ✅ | ASP.NET Identity default | Domyślnie brak limitu górnego w Identity |
| 2.1.3 | Weryfikacja że hasła nie są obcinane | ✅ | ASP.NET Identity | Identity nie obcina haseł |
| 2.1.4 | Weryfikacja że dowolne znaki Unicode są dozwolone | ✅ | ASP.NET Identity | Pełne wsparcie Unicode |
| 2.1.5 | Weryfikacja że użytkownicy mogą zmienić hasło | ✅ | `SportRental.Admin/Components/Account/Pages/Manage/` | Funkcjonalność zmiany hasła dostępna |
| 2.1.6 | Weryfikacja że zmiana hasła wymaga starego hasła | ✅ | Identity ChangePasswordAsync | Wymaga obecnego hasła |
| 2.1.7 | Weryfikacja że hasła są sprawdzane pod kątem breached passwords | ❌ | Brak implementacji | **MEDIUM:** Brak integracji z HaveIBeenPwned |
| 2.1.8 | Weryfikacja że podpowiedzi do hasła nie są używane | ✅ | Brak pola hint w ApplicationUser | Nie przechowuje podpowiedzi |
| 2.1.9 | Weryfikacja że hasła są hashowane z użyciem nowoczesnego algorytmu | ✅ | ASP.NET Identity PasswordHasher V3 | PBKDF2-HMAC-SHA256 |
| 2.1.10 | Weryfikacja że salt jest unikalny per użytkownik | ✅ | ASP.NET Identity | Automatyczny unikalny salt |
| 2.1.11 | Weryfikacja że work factor jest odpowiedni | ✅ | Identity PasswordHasher | 100,000 iteracji (domyślne) |
| 2.1.12 | Weryfikacja że użytkownik jest informowany o zmianie hasła | ⚠️ | Brak implementacji email alert | Brak powiadomień email o zmianie hasła |

### V2.2 General Authenticator Security

| ID | Wymaganie | Status | Lokalizacja w kodzie | Uwagi |
|----|-----------|--------|---------------------|-------|
| 2.2.1 | Weryfikacja że anty-automation jest stosowana | ⚠️ | `SportRental.Admin/Program.cs:104-118` | Admin ma rate limiting (100 req/min), API brak |
| 2.2.2 | Weryfikacja że słabe autentykatory są odrzucane | ❌ | `SportRental.Admin/Program.cs:247-251` | Admin akceptuje słabe hasła (6 znaków, brak complexity) |
| 2.2.2 | Weryfikacja że użytkownicy są powiadamiani o zmianach autentykacji | ❌ | Brak implementacji | Brak powiadomień o logowaniu/zmianach |
| 2.2.3 | Weryfikacja że odzyskiwanie konta jest bezpieczne | ✅ | Identity GeneratePasswordResetTokenAsync | Token-based recovery przez email |
| 2.2.4 | Weryfikacja że shared/default accounts nie są używane | ⚠️ | `SportRental.Admin/Program.cs:580,596,608` | Hardcoded test accounts w development seed |
| 2.2.5 | Weryfikacja że autentykacja failuje bezpiecznie | ✅ | `AuthEndpoints.cs:119-120` | Generyczne komunikaty błędów "Nieprawidłowy email lub hasło" |
| 2.2.6 | Weryfikacja że forgotten password nie ujawnia czy konto istnieje | ⚠️ | Password reset logic | Częściowo - timing attack możliwy |
| 2.2.7 | Weryfikacja że rate limiting jest stosowane | ⚠️ | `SportRental.Admin/Program.cs:104-118` | Admin: Fixed window 100/min, API: **BRAK** |

### V2.3 Authenticator Lifecycle

| ID | Wymaganie | Status | Lokalizacja w kodzie | Uwagi |
|----|-----------|--------|---------------------|-------|
| 2.3.1 | Weryfikacja że początkowe hasła są bezpiecznie generowane | ⚠️ | `EmployeeInvitation` entity | Zaproszenia generują tokeny, ale użytkownik ustawia hasło |
| 2.3.2 | Weryfikacja że enrollment i recovery credentials są bezpieczne | ✅ | `EmployeeInvitation`, Identity tokens | Kryptograficzne tokeny z expiration |
| 2.3.3 | Weryfikacja że renewal credentials nie ujawniają obecnych | ✅ | Password reset flow | Reset nie pokazuje obecnego hasła |

### V2.4 Credential Storage

| ID | Wymaganie | Status | Lokalizacja w kodzie | Uwagi |
|----|-----------|--------|---------------------|-------|
| 2.4.1 | Weryfikacja że hasła są hashowane z salt i pepper | ⚠️ | ASP.NET Identity PasswordHasher | Salt: TAK, Pepper: NIE (brak pepper) |
| 2.4.2 | Weryfikacja że salt jest co najmniej 32-bitowy | ✅ | Identity PasswordHasher V3 | 128-bit salt (16 bytes) |
| 2.4.3 | Weryfikacja że pepper jest używany dodatkowo do salt | ❌ | Brak implementacji | Brak application-wide pepper |
| 2.4.4 | Weryfikacja że work factor jest odpowiedni dla algorytmu | ✅ | Identity PasswordHasher | PBKDF2 100,000 iteracji |
| 2.4.5 | Weryfikacja że stary hash jest zmieniany przy logowaniu | ✅ | Identity automatyczne | Automatyczny upgrade hashów |

### V2.5 Credential Recovery

| ID | Wymaganie | Status | Lokalizacja w kodzie | Uwagi |
|----|-----------|--------|---------------------|-------|
| 2.5.1 | Weryfikacja że system nie ujawnia czy konto istnieje | ⚠️ | Password reset endpoint | Timing attack możliwy (różne czasy odpowiedzi) |
| 2.5.2 | Weryfikacja że recovery secrets są losowe i jednorazowe | ✅ | Identity DataProtection tokens | Kryptograficznie bezpieczne tokeny |
| 2.5.3 | Weryfikacja że recovery secrets wygasają | ✅ | Identity TokenLifespan | Domyślnie 24h expiration |
| 2.5.4 | Weryfikacja że recovery secrets są bezpiecznie przechowywane | ✅ | Identity token format | Tokeny nie są przechowywane (stateless) |
| 2.5.5 | Weryfikacja że recovery wymaga dodatkowej weryfikacji | ✅ | Email verification | Token wysyłany na email |
| 2.5.6 | Weryfikacja że OTP/MFA secrets są bezpiecznie przechowywane | ⚠️ | `SmsConfirmation` entity | Kody SMS w bazie - rozważyć hashowanie |

### V2.6 Look-up Secret Verifier

| ID | Wymaganie | Status | Lokalizacja w kodzie | Uwagi |
|----|-----------|--------|---------------------|-------|
| 2.6.1 | Weryfikacja że lookup secrets są unikalne i losowe | ⭕ | N/A | Aplikacja nie używa recovery codes |
| 2.6.2 | Weryfikacja że lookup secrets są jednorazowe | ⭕ | N/A | Aplikacja nie używa recovery codes |
| 2.6.3 | Weryfikacja że lookup secrets są odporne na offline attacks | ⭕ | N/A | Aplikacja nie używa recovery codes |

### V2.7 Out of Band Verifier

| ID | Wymaganie | Status | Lokalizacja w kodzie | Uwagi |
|----|-----------|--------|---------------------|-------|
| 2.7.1 | Weryfikacja że OOB authenticator jest bezpieczny | ✅ | SMS/Email przez HTTPS | Bezpieczne kanały komunikacji |
| 2.7.2 | Weryfikacja że OOB requests wygasają | ✅ | `SmsConfirmation.ExpiresAtUtc`, `RentalConfirmation` | Expiration zaimplementowane |
| 2.7.3 | Weryfikacja że OOB tokens są jednorazowe | ✅ | Confirmation logic z IsConfirmed flag | Jednorazowe użycie |
| 2.7.4 | Weryfikacja że OOB authenticator jest odporny na MITM | ✅ | HTTPS enforcement | TLS dla wszystkich połączeń |
| 2.7.5 | Weryfikacja że OOB verifier zachowuje tylko hash | ❌ | `SmsConfirmation.Code` plaintext | Kody SMS przechowywane jako plaintext |
| 2.7.6 | Weryfikacja że początkowy kod jest bezpiecznie losowy | ❌ | `SportRental.Admin/Services/Sms/SmsConfirmationService.cs:44-46` | Random.Shared (brak CSPRNG) |

### V2.8 One Time Verifier

| ID | Wymaganie | Status | Lokalizacja w kodzie | Uwagi |
|----|-----------|--------|---------------------|-------|
| 2.8.1 | Weryfikacja że OTP credentials są unikalne | ✅ | SMS code generation | Unikalne kody per sesja |
| 2.8.2 | Weryfikacja że OTP secrets są odporne na offline attacks | ❌ | Plaintext w bazie | Kody SMS nie są hashowane |
| 2.8.3 | Weryfikacja że OTP secrets są bezpiecznie przechowywane | ❌ | `SmsConfirmation.Code` | Plaintext storage - wymaga hashowania |
| 2.8.4 | Weryfikacja że OTP są oparte na czas lub licznik | ✅ | `ExpiresAtUtc` field | Time-based expiration |
| 2.8.5 | Weryfikacja że OTP mogą być użyte tylko raz | ✅ | `IsConfirmed` flag | Jednorazowe użycie |
| 2.8.6 | Weryfikacja że OTP nie mogą być ponownie użyte | ✅ | Confirmation check | Replay protection zaimplementowane |
| 2.8.7 | Weryfikacja że OTP są odporne na phishing | ⚠️ | Brak device binding | Brak powiązania z urządzeniem |

### V2.9 Cryptographic Verifier

| ID | Wymaganie | Status | Lokalizacja w kodzie | Uwagi |
|----|-----------|--------|---------------------|-------|
| 2.9.1 | Weryfikacja że klucze kryptograficzne są unikalne per użytkownik | ❌ | `JwtTokenService.cs:41` | Wspólny klucz signing dla wszystkich |
| 2.9.2 | Weryfikacja że challenge nonce jest co najmniej 64-bitowy | ✅ | `AuthEndpoints.cs:281` 64 bytes | 512-bit refresh token |
| 2.9.3 | Weryfikacja że zatwierdzone algorytmy są używane | ✅ | `JwtTokenService.cs:41` HmacSha256 | HMAC-SHA256 (standardowy) |

### V2.10 Service Authentication

| ID | Wymaganie | Status | Lokalizacja w kodzie | Uwagi |
|----|-----------|--------|---------------------|-------|
| 2.10.1 | Weryfikacja że service credentials nie są hardcoded | ❌ | `appsettings.json`, `appsettings.Development.json` | **CRITICAL:** Stripe, JWT, Azure, SMTP, SMS keys hardcoded |
| 2.10.2 | Weryfikacja że service credentials są bezpiecznie przechowywane | ❌ | Plaintext w repo | Key Vault skonfigurowany ale sekrety w plikach |
| 2.10.3 | Weryfikacja że service accounts mają minimalne uprawnienia | ⚠️ | Nieweryfikowalne z kodu | Wymaga przeglądu Azure/DB permissions |
| 2.10.4 | Weryfikacja że service credentials są rotowane | ❌ | Brak polityki rotacji | Brak automatycznej rotacji kluczy |

---

## V3: Session Management

### V3.1 Fundamental Session Management Security

| ID | Wymaganie | Status | Lokalizacja w kodzie | Uwagi |
|----|-----------|--------|---------------------|-------|
| 3.1.1 | Weryfikacja że aplikacja nie ujawnia session tokens w URL | ✅ | Cookie-based (Admin), JWT in Authorization header (API) | Tokeny w cookies/headers, nie w URL |

### V3.2 Session Binding

| ID | Wymaganie | Status | Lokalizacja w kodzie | Uwagi |
|----|-----------|--------|---------------------|-------|
| 3.2.1 | Weryfikacja że aplikacja generuje nowy session token przy logowaniu | ✅ | ASP.NET Identity SignInAsync | Nowy cookie przy logowaniu |
| 3.2.2 | Weryfikacja że session tokens są co najmniej 64-bitowe | ✅ | Identity cookie, JWT | Wysoka entropia tokenów |
| 3.2.2 | Weryfikacja że session tokens są przechowywane bezpiecznie | ✅ | `IdentityRedirectManager.cs:12-13` HttpOnly, SameSite=Strict | Bezpieczne flagi cookie |
| 3.2.3 | Weryfikacja że aplikacja używa tylko server-side session tokens | ✅ | Identity cookies (Admin), JWT stateless (API) | Server-side validation |
| 3.2.4 | Weryfikacja że session tokens są generowane kryptograficznie | ✅ | ASP.NET Identity DataProtection | Kryptograficznie bezpieczne |

### V3.3 Session Timeout

| ID | Wymaganie | Status | Lokalizacja w kodzie | Uwagi |
|----|-----------|--------|---------------------|-------|
| 3.3.1 | Weryfikacja że logout invaliduje sesję | ✅ | `IdentityComponentsEndpointRouteBuilderExtensions.cs:43-50` | SignOutAsync usuwa cookie |
| 3.3.2 | Weryfikacja że sesja wygasa po okresie nieaktywności | ⚠️ | Identity SlidingExpiration, JWT | Cookie sliding, JWT brak idle timeout |
| 3.3.3 | Weryfikacja że sesja wygasa po maksymalnym czasie | ✅ | `JwtOptions.cs:8-9` 120 min access, 7 dni refresh | Absolute expiration skonfigurowane |
| 3.3.4 | Weryfikacja że użytkownik może zakończyć wszystkie sesje | ✅ | `RefreshToken.RevokedAtUtc`, `/revoke` endpoint | Revoke all przez token revocation |

### V3.4 Cookie-based Session Management

| ID | Wymaganie | Status | Lokalizacja w kodzie | Uwagi |
|----|-----------|--------|---------------------|-------|
| 3.4.1 | Weryfikacja że cookie ma flagę Secure | ✅ | ASP.NET Identity default w HTTPS | Secure flag w produkcji |
| 3.4.2 | Weryfikacja że cookie ma flagę HttpOnly | ✅ | `IdentityRedirectManager.cs:13` HttpOnly = true | XSS protection |
| 3.4.3 | Weryfikacja że cookie ma atrybut SameSite | ✅ | `IdentityRedirectManager.cs:12` SameSite = Strict | CSRF protection |
| 3.4.4 | Weryfikacja że cookie ma odpowiednią ścieżkę | ✅ | ASP.NET Identity default path="/" | Odpowiedni scope |
| 3.4.5 | Weryfikacja że wrażliwe cookies mają atrybut __Host- | ❌ | Brak __Host- prefix | Brak cookie prefix security |

### V3.5 Token-based Session Management

| ID | Wymaganie | Status | Lokalizacja w kodzie | Uwagi |
|----|-----------|--------|---------------------|-------|
| 3.5.1 | Weryfikacja że aplikacja pozwala użytkownikowi odwołać tokeny | ✅ | `AuthEndpoints.cs` /revoke endpoint | Token revocation dostępne |
| 3.5.2 | Weryfikacja że aplikacja używa krótkotrwałych access tokens | ✅ | `JwtOptions.cs:8` 120 min (2h) | Krótkotrwałe access tokeny |
| 3.5.3 | Weryfikacja że refresh tokeny są bezpiecznie przechowywane | ❌ | `RefreshToken.cs:6-16`, `AuthEndpoints.cs:260-275` | Plaintext w DB - wymagane hashowanie |
| 3.5.4 | Weryfikacja że refresh tokeny mogą być odwołane | ✅ | `RefreshToken.IsRevoked`, `RevokedAtUtc` | Revocation mechanism |
| 3.5.5 | Weryfikacja że refresh tokeny są rotowane | ✅ | `AuthEndpoints.cs` refresh rotation | Rotacja przy każdym odświeżeniu |

### V3.6 Federated Re-authentication

| ID | Wymaganie | Status | Lokalizacja w kodzie | Uwagi |
|----|-----------|--------|---------------------|-------|
| 3.6.1 | Weryfikacja że wrażliwe operacje wymagają re-autentykacji | ❌ | Brak step-up auth | Brak re-auth dla krytycznych operacji |
| 3.6.2 | Weryfikacja że re-autentykacja jest wymagana przed zmianą credentials | ✅ | Identity ChangePasswordAsync | Wymaga obecnego hasła |

### V3.7 Defenses Against Session Management Exploits

| ID | Wymaganie | Status | Lokalizacja w kodzie | Uwagi |
|----|-----------|--------|---------------------|-------|
| 3.7.1 | Weryfikacja że aplikacja zapewnia CSRF protection | ✅ | `SportRental.Admin/Program.cs` UseAntiforgery() | Anti-forgery tokens aktywne |
| 3.7.2 | Weryfikacja że aplikacja nie jest podatna na session fixation | ✅ | Identity regeneruje cookie przy login | Nowa sesja przy autentykacji |

---

## V4: Access Control

### V4.1 General Access Control Design

| ID | Wymaganie | Status | Lokalizacja w kodzie | Uwagi |
|----|-----------|--------|---------------------|-------|
| 4.1.1 | Weryfikacja że kontrola dostępu jest egzekwowana na zaufanej warstwie | ✅ | `[Authorize]` atrybuty, Query Filters | Server-side enforcement |
| 4.1.2 | Weryfikacja że wszystkie atrybuty użytkownika są bezpiecznie przechowywane | ✅ | `ApplicationUser` w AspNetUsers | Bezpieczne przechowywanie w DB |
| 4.1.3 | Weryfikacja że principle of least privilege jest stosowany | ✅ | Role hierarchy: SuperAdmin>Owner>Employee>Client | Minimalne uprawnienia per rola |
| 4.1.4 | Weryfikacja że kontrola dostępu failuje bezpiecznie | ✅ | ASP.NET Authorization default deny | Deny by default |
| 4.1.5 | Weryfikacja że ten sam mechanizm kontroli jest używany wszędzie | ✅ | Identity + Query Filters + [Authorize] | Ujednolicone podejście |

### V4.2 Operation Level Access Control

| ID | Wymaganie | Status | Lokalizacja w kodzie | Uwagi |
|----|-----------|--------|---------------------|-------|
| 4.2.1 | Weryfikacja że wrażliwe dane i API są chronione przed IDOR | ✅ | `ApplicationDbContext.cs` TenantId query filters | Multi-tenant IDOR protection |
| 4.2.2 | Weryfikacja że aplikacja używa silnych mechanizmów anty-CSRF | ✅ | `UseAntiforgery()`, SameSite cookies | CSRF protection aktywne |

### V4.3 Other Access Control Considerations

| ID | Wymaganie | Status | Lokalizacja w kodzie | Uwagi |
|----|-----------|--------|---------------------|-------|
| 4.3.1 | Weryfikacja że administrative interfaces używają MFA | ❌ | Brak MFA | **MEDIUM:** Admin panel bez MFA |
| 4.3.2 | Weryfikacja że directory browsing jest wyłączony | ✅ | ASP.NET Core default | Directory browsing domyślnie wyłączony |
| 4.3.3 | Weryfikacja że aplikacja ma autoryzację dla każdego workflow | ⚠️ | `Endpoints.cs` - niektóre [AllowAnonymous] | Niektóre endpointy publiczne bez uzasadnienia |

---

## V5: Validation, Sanitization and Encoding

### V5.1 Input Validation

| ID | Wymaganie | Status | Lokalizacja w kodzie | Uwagi |
|----|-----------|--------|---------------------|-------|
| 5.1.1 | Weryfikacja że aplikacja ma defenses przeciwko HTTP parameter pollution | ✅ | ASP.NET Core model binding | Framework obsługuje HPP |
| 5.1.2 | Weryfikacja że frameworks chronią przed mass assignment | ✅ | DTOs w `SportRental.Shared/Models/` | Explicit DTOs, nie entities |
| 5.1.3 | Weryfikacja że input validation jest stosowany na zaufanej warstwie | ✅ | `Endpoints.cs:522-536` server validation | Server-side validation |
| 5.1.4 | Weryfikacja że input validation używa pozytywnej walidacji | ⚠️ | Częściowo - brak validation attributes | Wymaga dodania DataAnnotations |
| 5.1.5 | Weryfikacja że output encoding jest stosowany | ✅ | Blazor automatic encoding | Automatyczne enkodowanie |

### V5.2 Sanitization and Sandboxing

| ID | Wymaganie | Status | Lokalizacja w kodzie | Uwagi |
|----|-----------|--------|---------------------|-------|
| 5.2.1 | Weryfikacja że niezaufany HTML jest sanitized | ✅ | Blazor nie renderuje raw HTML | Brak MarkupString z user input |
| 5.2.2 | Weryfikacja że unvalidated redirects są unikane | ✅ | `TypedResults.LocalRedirect()` | Walidowane przekierowania |
| 5.2.3 | Weryfikacja że aplikacja unika eval() i podobnych | ✅ | Brak dynamic code execution | .NET nie używa eval |
| 5.2.4 | Weryfikacja że template injection jest zapobiegany | ✅ | Blazor/Razor precompiled | Brak runtime template injection |
| 5.2.5 | Weryfikacja że SSRF jest zapobiegany | ⚠️ | HttpClient usage | Brak explicit URL validation |
| 5.2.6 | Weryfikacja że aplikacja sanitizuje, disables, lub sandboxes user-supplied SVG | ✅ | `Endpoints.cs:728` dozwolone rozszerzenia | SVG dozwolone ale bez execution |
| 5.2.7 | Weryfikacja że aplikacja sanitizuje user-supplied scriptable content | ✅ | Blazor encoding, brak user scripts | Brak user-supplied scripts |
| 5.2.8 | Weryfikacja że aplikacja chroni przed LDAP injection | ⭕ | N/A | Aplikacja nie używa LDAP |

### V5.3 Output Encoding and Injection Prevention

| ID | Wymaganie | Status | Lokalizacja w kodzie | Uwagi |
|----|-----------|--------|---------------------|-------|
| 5.3.1 | Weryfikacja że output encoding jest kontekstowy | ✅ | Blazor/Razor context-aware | HTML/JS/URL encoding |
| 5.3.2 | Weryfikacja że output encoding zachowuje user's chosen character set | ✅ | UTF-8 throughout | Spójne kodowanie UTF-8 |
| 5.3.3 | Weryfikacja że context-aware escaping chroni przed XSS | ✅ | Blazor automatic escaping | XSS protection |
| 5.3.4 | Weryfikacja że data selection używa parameterized queries | ✅ | EF Core LINQ everywhere | SQL injection prevention |
| 5.3.5 | Weryfikacja że aplikacja chroni przed template injection | ✅ | Razor precompiled templates | SSTI protection |
| 5.3.6 | Weryfikacja że aplikacja chroni przed SSRF | ⚠️ | HttpClient bez URL allowlist | Brak explicit SSRF protection |
| 5.3.7 | Weryfikacja że aplikacja chroni przed XPath/XML injection | ⭕ | N/A | Brak XML processing |
| 5.3.8 | Weryfikacja że aplikacja chroni przed JSON injection | ✅ | System.Text.Json | Bezpieczna deserializacja |
| 5.3.9 | Weryfikacja że aplikacja chroni przed LDAP injection | ⭕ | N/A | Brak LDAP |
| 5.3.10 | Weryfikacja że aplikacja chroni przed OS command injection | ✅ | Brak Process.Start | Brak wykonywania komend |

### V5.4 Memory, String, and Unmanaged Code

| ID | Wymaganie | Status | Lokalizacja w kodzie | Uwagi |
|----|-----------|--------|---------------------|-------|
| 5.4.1 | Weryfikacja że aplikacja używa memory-safe string operations | ✅ | .NET managed code | Memory safety przez CLR |
| 5.4.2 | Weryfikacja że format strings nie przyjmują user input | ✅ | Interpolacja stringów | Brak format string vulnerabilities |
| 5.4.3 | Weryfikacja że sign, range, i input validation są stosowane | ✅ | `Endpoints.cs:528` quantity > 0 checks | Walidacja zakresów |

### V5.5 Deserialization Prevention

| ID | Wymaganie | Status | Lokalizacja w kodzie | Uwagi |
|----|-----------|--------|---------------------|-------|
| 5.5.1 | Weryfikacja że serialized objects używają integrity checks | ✅ | JWT podpisany, DTOs typed | Integralność przez podpisy |
| 5.5.2 | Weryfikacja że aplikacja ogranicza parsery XML | ⭕ | N/A | Brak XML processing |
| 5.5.3 | Weryfikacja że deserialization niezaufanych danych jest unikana | ✅ | System.Text.Json strict | Brak polymorphic deserialization |
| 5.5.4 | Weryfikacja że JSON jest parsowany bezpiecznie | ✅ | System.Text.Json default | Bezpieczne parsowanie JSON |

---

## V6: Stored Cryptography

### V6.1 Data Classification

| ID | Wymaganie | Status | Lokalizacja w kodzie | Uwagi |
|----|-----------|--------|---------------------|-------|
| 6.1.1 | Weryfikacja że regulated private data jest przechowywana zaszyfrowana | ⚠️ | Customer PII w bazie | PII w plaintext - rozważyć szyfrowanie |
| 6.1.2 | Weryfikacja że regulated health data jest przechowywana zaszyfrowana | ⭕ | N/A | Aplikacja nie przetwarza danych zdrowotnych |
| 6.1.3 | Weryfikacja że regulated financial data jest przechowywane zaszyfrowane | ✅ | Stripe handles payment data | Dane kart przez Stripe, nie lokalnie |

### V6.2 Algorithms

| ID | Wymaganie | Status | Lokalizacja w kodzie | Uwagi |
|----|-----------|--------|---------------------|-------|
| 6.2.1 | Weryfikacja że wszystkie moduły kryptograficzne failują bezpiecznie | ✅ | .NET crypto exceptions | Bezpieczne failure z exceptions |
| 6.2.2 | Weryfikacja że industry-proven RNG jest używany | ✅ | `RandomNumberGenerator.Create()` | CSPRNG z .NET |
| 6.2.3 | Weryfikacja że zatwierdzone algorytmy są używane | ✅ | HMAC-SHA256, PBKDF2 | Standardowe algorytmy |
| 6.2.4 | Weryfikacja że insecure block modes nie są używane | ✅ | Brak custom encryption | Brak ECB/CBC w kodzie |
| 6.2.5 | Weryfikacja że nonces, IVs są generowane raz per encryption key | ✅ | DataProtection handles | Framework zarządza IV |
| 6.2.6 | Weryfikacja że RNG jest odpowiednio seeded | ✅ | OS entropy source | .NET używa systemowego RNG |
| 6.2.7 | Weryfikacja że zatwierdzone modes są używane | ✅ | DataProtection AES-GCM | Authenticated encryption |
| 6.2.8 | Weryfikacja że nonces, IVs, i inne single-use numbers nie są używane więcej niż raz | ✅ | Framework handles | Automatyczna unikalność |

### V6.3 Random Values

| ID | Wymaganie | Status | Lokalizacja w kodzie | Uwagi |
|----|-----------|--------|---------------------|-------|
| 6.3.1 | Weryfikacja że wszystkie random numbers są kryptograficznie bezpieczne | ✅ | `AuthEndpoints.cs:280-284` | RandomNumberGenerator dla tokenów |
| 6.3.2 | Weryfikacja że random GUIDs są tworzone z CSPRNG | ✅ | `Guid.NewGuid()` | .NET GUID z CSPRNG |
| 6.3.3 | Weryfikacja że random strings mają wystarczającą entropię | ✅ | 64 bytes dla refresh token | 512 bitów entropii |

### V6.4 Secret Management

| ID | Wymaganie | Status | Lokalizacja w kodzie | Uwagi |
|----|-----------|--------|---------------------|-------|
| 6.4.1 | Weryfikacja że secrets nie są hardcoded | ❌ | `appsettings.json`, `appsettings.Development.json` | **CRITICAL:** Sekrety hardcoded w repo |
| 6.4.2 | Weryfikacja że klucze kryptograficzne są zarządzane w key vault | ❌ | Key Vault URL skonfigurowany ale nie używany | Sekrety w plikach zamiast Key Vault |

---

## V7: Error Handling and Logging

### V7.1 Log Content

| ID | Wymaganie | Status | Lokalizacja w kodzie | Uwagi |
|----|-----------|--------|---------------------|-------|
| 7.1.1 | Weryfikacja że aplikacja nie loguje credentials | ✅ | ILogger usage | Brak logowania haseł |
| 7.1.2 | Weryfikacja że aplikacja nie loguje innych wrażliwych danych | ⚠️ | Logging configuration | Możliwe logowanie PII bez redakcji |
| 7.1.3 | Weryfikacja że aplikacja loguje security events | ✅ | `AuditLog` entity | Audit trail zaimplementowany |
| 7.1.4 | Weryfikacja że każdy log entry ma wystarczający kontekst | ✅ | Structured logging z ILogger | Kontekstowe logowanie |

### V7.2 Log Processing

| ID | Wymaganie | Status | Lokalizacja w kodzie | Uwagi |
|----|-----------|--------|---------------------|-------|
| 7.2.1 | Weryfikacja że wszystkie authentication decisions są logowane | ⚠️ | Identity events | Częściowe - brak explicit audit log |
| 7.2.2 | Weryfikacja że wszystkie access control failures są logowane | ⚠️ | Authorization middleware | Framework loguje 403, brak custom audit |

### V7.3 Log Protection

| ID | Wymaganie | Status | Lokalizacja w kodzie | Uwagi |
|----|-----------|--------|---------------------|-------|
| 7.3.1 | Weryfikacja że wszystkie logi są chronione przed unauthorized access | ✅ | Database + file permissions | Kontrola dostępu do logów |
| 7.3.2 | Weryfikacja że wszystkie logi są chronione przed unauthorized modification | ⚠️ | Brak append-only | Logi modyfikowalne |
| 7.3.3 | Weryfikacja że time sources są zsynchronizowane | ✅ | `DateTime.UtcNow` everywhere | Spójne UTC timestamps |
| 7.3.4 | Weryfikacja że logi są przechowywane w różnych lokalizacjach | ⚠️ | Lokalne pliki/DB | Brak distributed logging |

### V7.4 Error Handling

| ID | Wymaganie | Status | Lokalizacja w kodzie | Uwagi |
|----|-----------|--------|---------------------|-------|
| 7.4.1 | Weryfikacja że generic error messages są wyświetlane użytkownikowi | ✅ | `Program.cs:341-345` UseExceptionHandler | Generyczne błędy w produkcji |
| 7.4.2 | Weryfikacja że exception handling jest używany | ✅ | Try-catch + middleware | Kompleksowa obsługa błędów |
| 7.4.3 | Weryfikacja że "last resort" error handler jest zdefiniowany | ✅ | UseExceptionHandler("/Error") | Global exception handler |
| 7.4.4 | Weryfikacja że memory dumps nie zawierają wrażliwych danych | ⚠️ | Nieweryfikowalne statycznie | Wymaga konfiguracji deployment |

---

## V8: Data Protection

### V8.1 General Data Protection

| ID | Wymaganie | Status | Lokalizacja w kodzie | Uwagi |
|----|-----------|--------|---------------------|-------|
| 8.1.1 | Weryfikacja że aplikacja chroni wrażliwe dane przed cache | ⚠️ | Brak explicit Cache-Control | Wymaga dodania no-store headers |
| 8.1.2 | Weryfikacja że aplikacja chroni wrażliwe dane przed storage | ⚠️ | DB bez encryption at rest | Zależy od konfiguracji PostgreSQL |
| 8.1.3 | Weryfikacja że wrażliwe dane są czyszczone z pamięci | ⚠️ | .NET GC | Brak explicit SecureString |
| 8.1.4 | Weryfikacja że wrażliwe dane są przesyłane przez HTTPS | ✅ | `UseHttpsRedirection()`, HSTS | HTTPS wymuszony |
| 8.1.5 | Weryfikacja że wrażliwe dane nie są przesyłane w GET | ✅ | POST dla login/register | Wrażliwe dane w body |
| 8.1.6 | Weryfikacja że authenticated cookies mają Secure flag | ✅ | ASP.NET Identity default | Secure flag w produkcji |

### V8.2 Client-side Data Protection

| ID | Wymaganie | Status | Lokalizacja w kodzie | Uwagi |
|----|-----------|--------|---------------------|-------|
| 8.2.1 | Weryfikacja że aplikacja ustawia sufficient anti-caching headers | ⚠️ | Brak explicit headers | Wymaga Cache-Control: no-store |
| 8.2.2 | Weryfikacja że dane w browser storage nie zawierają wrażliwych danych | ❌ | `SportRental.Client/Services/ApiAuthenticationStateProvider.cs:24-152` | authToken/refreshToken w localStorage |
| 8.2.3 | Weryfikacja że authenticated data jest czyszczona po logout | ✅ | SignOutAsync + cookie clear | Czyszczenie przy logout |

### V8.3 Sensitive Private Data

| ID | Wymaganie | Status | Lokalizacja w kodzie | Uwagi |
|----|-----------|--------|---------------------|-------|
| 8.3.1 | Weryfikacja że wrażliwe dane są przesyłane do serwera w HTTP body | ✅ | POST requests | Dane w body nie URL |
| 8.3.2 | Weryfikacja że wrażliwe dane nie są logowane | ⚠️ | ILogger usage | Możliwe PII w logach |
| 8.3.3 | Weryfikacja że wrażliwe dane nie są sharowane z third parties | ✅ | Stripe only | Tylko Stripe otrzymuje dane płatnicze |
| 8.3.4 | Weryfikacja że wrażliwe dane nie są included w HTTP cache | ⚠️ | Brak no-store | Wymaga Cache-Control headers |
| 8.3.5 | Weryfikacja że wrażliwe dane nie są included w backups | ⚠️ | Zależy od ops | Wymaga szyfrowania backupów |
| 8.3.6 | Weryfikacja że temporary buffers są czyszczone | ⚠️ | .NET GC | Brak explicit clearing |
| 8.3.7 | Weryfikacja że wrażliwe dane są chronione w memory dumps | ⚠️ | Nieweryfikowalne | Wymaga konfiguracji prod |
| 8.3.8 | Weryfikacja że wrażliwe informacje w memory są overwritten | ⚠️ | Brak SecureString | .NET GC nie zeruje pamięci |

---

## V9: Communication

### V9.1 Client Communication Security

| ID | Wymaganie | Status | Lokalizacja w kodzie | Uwagi |
|----|-----------|--------|---------------------|-------|
| 9.1.1 | Weryfikacja że TLS jest używany dla wszystkich połączeń | ✅ | `UseHttpsRedirection()`, HSTS | HTTPS wymuszony |
| 9.1.2 | Weryfikacja że najnowsze wersje TLS są używane | ✅ | .NET 8 defaults | TLS 1.2+ domyślnie |
| 9.1.3 | Weryfikacja że stare wersje TLS są wyłączone | ✅ | .NET 8 defaults | SSLv3, TLS 1.0/1.1 wyłączone |

### V9.2 Server Communication Security

| ID | Wymaganie | Status | Lokalizacja w kodzie | Uwagi |
|----|-----------|--------|---------------------|-------|
| 9.2.1 | Weryfikacja że connections do/z serwera używają trusted TLS certificates | ✅ | Azure hosting | Zaufane certyfikaty |
| 9.2.2 | Weryfikacja że encrypted communications są używane dla wszystkich inbound/outbound | ✅ | HTTPS, DB connection | Szyfrowane połączenia |
| 9.2.3 | Weryfikacja że wszystkie encrypted connections do external systems są authenticated | ✅ | Stripe, SMTP over TLS | API keys + TLS |
| 9.2.4 | Weryfikacja że proper certification revocation jest enabled | ✅ | .NET default behavior | CRL/OCSP checking |
| 9.2.5 | Weryfikacja że backend TLS connection failures są logowane | ⚠️ | Framework logging | Brak explicit TLS logging |

---

## V10: Malicious Code

### V10.1 Code Integrity

| ID | Wymaganie | Status | Lokalizacja w kodzie | Uwagi |
|----|-----------|--------|---------------------|-------|
| 10.1.1 | Weryfikacja że code analysis tool jest używany | ⚠️ | `.github/workflows/ci.yml` | CI istnieje, brak explicit SAST |

### V10.2 Malicious Code Search

| ID | Wymaganie | Status | Lokalizacja w kodzie | Uwagi |
|----|-----------|--------|---------------------|-------|
| 10.2.1 | Weryfikacja że source code nie zawiera time bombs | ✅ | Code review | Brak time bombów w kodzie |
| 10.2.2 | Weryfikacja że aplikacja nie ma backdoors | ✅ | Code review | Brak backdoorów |
| 10.2.3 | Weryfikacja że aplikacja nie ma Easter eggs | ✅ | Code review | Brak ukrytej funkcjonalności |
| 10.2.4 | Weryfikacja że source code nie zawiera malicious code | ✅ | Code review | Brak złośliwego kodu |
| 10.2.5 | Weryfikacja że aplikacja nie używa undocumented features | ✅ | Standard .NET APIs | Standardowe API |
| 10.2.6 | Weryfikacja że third-party libraries są reviewed | ⚠️ | NuGet packages | Brak dependency scanning |

### V10.3 Application Integrity

| ID | Wymaganie | Status | Lokalizacja w kodzie | Uwagi |
|----|-----------|--------|---------------------|-------|
| 10.3.1 | Weryfikacja że aplikacja ma auto-update feature z signature verification | ⭕ | N/A | Web app - brak client updates |
| 10.3.2 | Weryfikacja że aplikacja używa code signing | ⚠️ | Docker images | Brak explicit signing |
| 10.3.3 | Weryfikacja że aplikacja nie używa unauthorized third-party code | ✅ | NuGet.org only | Oficjalne źródła pakietów |

---

## V11: Business Logic

### V11.1 Business Logic Security

| ID | Wymaganie | Status | Lokalizacja w kodzie | Uwagi |
|----|-----------|--------|---------------------|-------|
| 11.1.1 | Weryfikacja że aplikacja egzekwuje business logic flow | ✅ | `Endpoints.cs:567-580` transakcje | Serializable isolation level |
| 11.1.2 | Weryfikacja że aplikacja egzekwuje limity na business actions | ✅ | `Endpoints.cs:528-536` walidacja | Limity ilości, dat |
| 11.1.3 | Weryfikacja że aplikacja ma anti-automation controls | ⚠️ | Rate limiting tylko Admin | API brak rate limiting |
| 11.1.4 | Weryfikacja że aplikacja ma business logic limits | ✅ | Hold TTL 5-30 min, quantity checks | Limity biznesowe |
| 11.1.5 | Weryfikacja że aplikacja chroni przed race conditions | ✅ | `IsolationLevel.Serializable` | Transakcje z blokadami |
| 11.1.6 | Weryfikacja że aplikacja monitoruje unusual business logic events | ✅ | `AuditLog` entity | Audit logging |
| 11.1.7 | Weryfikacja że aplikacja ma alerts dla automated attacks | ❌ | Brak alerting | Brak systemu alertów |
| 11.1.8 | Weryfikacja że aplikacja ma configurable alerting thresholds | ❌ | Brak implementation | Brak konfiguracji alertów |

---

## V12: Files and Resources

### V12.1 File Upload

| ID | Wymaganie | Status | Lokalizacja w kodzie | Uwagi |
|----|-----------|--------|---------------------|-------|
| 12.1.1 | Weryfikacja że aplikacja nie akceptuje large files | ✅ | `Endpoints.cs:706`, `FileStorageService.cs:163` | Max 5MB default, konfigurowalne |
| 12.1.2 | Weryfikacja że aplikacja sprawdza compressed files | ⭕ | N/A | Brak uploadu archiwum |
| 12.1.3 | Weryfikacja że file size quota jest egzekwowana | ✅ | `StorageOptions.MaxFileSizeBytes` | Per-file quota |

### V12.2 File Integrity

| ID | Wymaganie | Status | Lokalizacja w kodzie | Uwagi |
|----|-----------|--------|---------------------|-------|
| 12.2.1 | Weryfikacja że files obtained from untrusted sources są validated | ✅ | `FileStorageService.cs:156-172` | Extension + size validation |

### V12.3 File Execution

| ID | Wymaganie | Status | Lokalizacja w kodzie | Uwagi |
|----|-----------|--------|---------------------|-------|
| 12.3.1 | Weryfikacja że user-submitted filename metadata nie jest używane | ✅ | `FileStorageService.cs:127-128` | GUID jako nazwa pliku |
| 12.3.2 | Weryfikacja że user-submitted files nie są wykonywane | ✅ | Static file serving | Brak execution |
| 12.3.3 | Weryfikacja że user-submitted files są przechowywane poza webroot | ✅ | Azure Blob / App_Data | Izolowane storage |
| 12.3.4 | Weryfikacja że aplikacja chroni przed file inclusion | ✅ | `FileStorageService.cs:105-108` | Blokada ".." w ścieżkach |
| 12.3.5 | Weryfikacja że untrusted data nie jest używane w dynamic includes | ✅ | Brak dynamic includes | Brak code injection risk |
| 12.3.6 | Weryfikacja że aplikacja chroni przed SSRF | ⚠️ | HttpClient usage | Brak URL allowlist |

### V12.4 File Storage

| ID | Wymaganie | Status | Lokalizacja w kodzie | Uwagi |
|----|-----------|--------|---------------------|-------|
| 12.4.1 | Weryfikacja że files obtained from untrusted sources są stored poza webroot | ✅ | Azure Blob / App_Data | Izolowane storage |
| 12.4.2 | Weryfikacja że files obtained from untrusted sources są scanned | ❌ | Brak AV | Brak skanowania malware |

### V12.5 File Download

| ID | Wymaganie | Status | Lokalizacja w kodzie | Uwagi |
|----|-----------|--------|---------------------|-------|
| 12.5.1 | Weryfikacja że aplikacja chroni przed path traversal | ✅ | `FileStorageService.cs:105-108` | Blokada ".." traversal |
| 12.5.2 | Weryfikacja że aplikacja chroni przed file inclusion | ✅ | Normalized paths | LFI prevention |

### V12.6 SSRF Protection

| ID | Wymaganie | Status | Lokalizacja w kodzie | Uwagi |
|----|-----------|--------|---------------------|-------|
| 12.6.1 | Weryfikacja że web/application server jest configured with allowlist | ⚠️ | HttpClient bez restrictions | Brak URL allowlist |

---

## V13: API and Web Service

### V13.1 Generic Web Service Security

| ID | Wymaganie | Status | Lokalizacja w kodzie | Uwagi |
|----|-----------|--------|---------------------|-------|
| 13.1.1 | Weryfikacja że wszystkie application components używają tego samego encodings | ✅ | UTF-8 everywhere | Spójne kodowanie UTF-8 |
| 13.1.2 | Weryfikacja że access to administration i management functions jest limited | ✅ | `[Authorize(Roles = "Owner")]` | Role-based admin access |
| 13.1.3 | Weryfikacja że API URLs nie expose sensitive information | ✅ | GUIDs w URL, nie dane | Brak wrażliwych danych w URL |
| 13.1.4 | Weryfikacja że authorization decisions są made at URI i resource level | ✅ | [Authorize] + Query Filters | Autoryzacja na poziomie endpoint + zasobu |
| 13.1.5 | Weryfikacja że requests containing unexpected content types są rejected | ⚠️ | ASP.NET model binding | Brak strict Content-Type validation |

### V13.2 RESTful Web Service

| ID | Wymaganie | Status | Lokalizacja w kodzie | Uwagi |
|----|-----------|--------|---------------------|-------|
| 13.2.1 | Weryfikacja że enabled HTTP methods są valid choice | ✅ | MapGet/Post/Delete explicit | Tylko potrzebne metody HTTP |
| 13.2.2 | Weryfikacja że JSON schema validation jest in place | ⚠️ | Model binding | Brak explicit JSON schema validation |
| 13.2.3 | Weryfikacja że RESTful web services używają anti-automation controls | ⚠️ | Admin rate limiting tylko | **MEDIUM:** API bez rate limiting |
| 13.2.4 | Weryfikacja że REST services explicitly check incoming Content-Type | ⚠️ | ASP.NET implicit | Brak explicit Content-Type check |
| 13.2.5 | Weryfikacja że message headers i payload są trustworthy | ✅ | Model binding + validation | Server-side validation |
| 13.2.6 | Weryfikacja że alternative i less secure access paths nie exist | ✅ | Unified API structure | Brak backdoor API |

### V13.3 SOAP Web Service

| ID | Wymaganie | Status | Lokalizacja w kodzie | Uwagi |
|----|-----------|--------|---------------------|-------|
| 13.3.1 | Weryfikacja że XSD schema validation takes place | ⭕ | N/A | Aplikacja nie używa SOAP |
| 13.3.2 | Weryfikacja że SOAP payload jest signed | ⭕ | N/A | Aplikacja nie używa SOAP |

### V13.4 GraphQL

| ID | Wymaganie | Status | Lokalizacja w kodzie | Uwagi |
|----|-----------|--------|---------------------|-------|
| 13.4.1 | Weryfikacja że query allowlisting lub depth/amount limiting jest used | ⭕ | N/A | Aplikacja nie używa GraphQL |
| 13.4.2 | Weryfikacja że GraphQL authorization logic jest implemented | ⭕ | N/A | Aplikacja nie używa GraphQL |

---

## V14: Configuration

### V14.1 Build and Deploy

| ID | Wymaganie | Status | Lokalizacja w kodzie | Uwagi |
|----|-----------|--------|---------------------|-------|
| 14.1.1 | Weryfikacja że application build i deployment processes są performed securely | ✅ | `.github/workflows/ci.yml` | CI/CD przez GitHub Actions |
| 14.1.2 | Weryfikacja że compiler flags są configured to enable security protections | ✅ | .NET 8 defaults | Nowoczesne ustawienia kompilatora |
| 14.1.3 | Weryfikacja że application configuration jest stored securely | ❌ | `appsettings.json` secrets | **CRITICAL:** Sekrety w plaintext |
| 14.1.4 | Weryfikacja że third-party components come from trusted repositories | ✅ | NuGet.org | Oficjalne źródła |
| 14.1.5 | Weryfikacja że build pipeline warns of out-of-date components | ⚠️ | Brak Dependabot | Brak automatycznego sprawdzania |
| 14.1.6 | Weryfikacja że application does not use unsupported technologies | ✅ | .NET 8 LTS | Wspierana wersja framework |

### V14.2 Dependency

| ID | Wymaganie | Status | Lokalizacja w kodzie | Uwagi |
|----|-----------|--------|---------------------|-------|
| 14.2.1 | Weryfikacja że all components są up to date | ⚠️ | NuGet packages | Wymaga regularnego przeglądu |
| 14.2.2 | Weryfikacja że all unneeded features są removed | ✅ | Minimal dependencies | Tylko potrzebne pakiety |
| 14.2.3 | Weryfikacja że application only uses third-party components from trusted sources | ✅ | NuGet.org | Oficjalne źródła |
| 14.2.4 | Weryfikacja że inventory of third-party libraries jest maintained | ❌ | Brak SBOM | Brak Software Bill of Materials |
| 14.2.5 | Weryfikacja że attack surface jest reduced by sandboxing third-party libraries | ⚠️ | .NET isolation | Standardowa izolacja .NET |
| 14.2.6 | Weryfikacja że application does not use unsupported third-party components | ⚠️ | Nieweryfikowane | Wymaga dependency scan |

### V14.3 Unintended Security Disclosure

| ID | Wymaganie | Status | Lokalizacja w kodzie | Uwagi |
|----|-----------|--------|---------------------|-------|
| 14.3.1 | Weryfikacja że web/application server i framework error messages są configured to deliver safe messages | ✅ | UseExceptionHandler | Generyczne błędy w prod |
| 14.3.2 | Weryfikacja że web/application server jest configured to prevent disclosure | ⚠️ | Swagger w produkcji | **HIGH:** Swagger dostępny bez auth |
| 14.3.3 | Weryfikacja że HTTP headers do not expose detailed version information | ⚠️ | ASP.NET defaults | Server header może ujawniać wersję |
| 14.3.4 | Weryfikacja że directory browsing jest disabled | ✅ | ASP.NET default | Directory listing wyłączony |
| 14.3.5 | Weryfikacja że HTTP headers do not expose other system information | ⚠️ | Brak header hardening | Wymaga usunięcia Server header |

### V14.4 HTTP Security Headers

| ID | Wymaganie | Status | Lokalizacja w kodzie | Uwagi |
|----|-----------|--------|---------------------|-------|
| 14.4.1 | Weryfikacja że every HTTP response contains Content-Type header | ✅ | ASP.NET automatic | Content-Type zawsze obecny |
| 14.4.2 | Weryfikacja że all API responses contain Content-Disposition: attachment | ⚠️ | Tylko dla plików | Nie dla wszystkich odpowiedzi |
| 14.4.3 | Weryfikacja że Content Security Policy header jest included | ❌ | Brak CSP | **HIGH:** Brak Content-Security-Policy |
| 14.4.4 | Weryfikacja że all responses contain X-Content-Type-Options: nosniff | ❌ | Brak header | **MEDIUM:** Brak X-Content-Type-Options |
| 14.4.5 | Weryfikacja że HTTP Strict Transport Security header jest included | ✅ | `UseHsts()` | HSTS aktywne |
| 14.4.6 | Weryfikacja że Referrer-Policy header jest included | ❌ | Brak header | **MEDIUM:** Brak Referrer-Policy |
| 14.4.7 | Weryfikacja że suitable X-Frame-Options header jest included | ❌ | Brak header | **MEDIUM:** Brak X-Frame-Options |

### V14.5 HTTP Request Header Validation

| ID | Wymaganie | Status | Lokalizacja w kodzie | Uwagi |
|----|-----------|--------|---------------------|-------|
| 14.5.1 | Weryfikacja że application server accepts only HTTP methods in use | ✅ | Explicit MapGet/Post/Delete | Tylko zdefiniowane metody |
| 14.5.2 | Weryfikacja że supplied Origin header nie jest used for authentication | ✅ | JWT + cookies | Origin nie używany do auth |
| 14.5.3 | Weryfikacja że Cross-Origin Resource Sharing Access-Control-Allow-Origin header uses strict allowlist | ❌ | `Program.cs:196-198` AllowAnyOrigin | **HIGH:** CORS AllowAnyOrigin w prod |
| 14.5.4 | Weryfikacja że HTTP headers added by trusted proxy są authenticated | ⚠️ | Brak ForwardedHeaders config | Wymaga konfiguracji proxy |

---

## Podsumowanie audytu

### Statystyki

| Kategoria | Total | Pass | Fail | Partial | N/A |
|-----------|-------|------|------|---------|-----|
| V1: Architecture | 16 | 10 | 3 | 3 | 0 |
| V2: Authentication | 44 | 23 | 13 | 5 | 3 |
| V3: Session Management | 18 | 14 | 3 | 1 | 0 |
| V4: Access Control | 8 | 6 | 1 | 1 | 0 |
| V5: Validation | 22 | 18 | 0 | 2 | 2 |
| V6: Cryptography | 14 | 10 | 2 | 1 | 1 |
| V7: Error Handling | 12 | 6 | 0 | 6 | 0 |
| V8: Data Protection | 17 | 5 | 1 | 10 | 1 |
| V9: Communication | 8 | 7 | 0 | 1 | 0 |
| V10: Malicious Code | 10 | 6 | 0 | 3 | 1 |
| V11: Business Logic | 8 | 5 | 2 | 1 | 0 |
| V12: Files and Resources | 13 | 9 | 1 | 2 | 1 |
| V13: API Security | 13 | 6 | 0 | 3 | 4 |
| V14: Configuration | 18 | 7 | 5 | 6 | 0 |
| **TOTAL** | **221** | **132** | **31** | **45** | **13** |

### Compliance Score

**ASVS Level 2 Compliance:** **63%** (132 PASS / 208 applicable)

### Krytyczne znaleziska (do natychmiastowej naprawy)

1. **SEC-001 (1.2.3, 2.10.1, 6.4.1, 14.1.3):** Hardcoded secrets w `appsettings.json` - Stripe keys, JWT signing key, Azure Blob connection strings, SMTP passwords, SMS API credentials
2. **SEC-002 (2.1.1, 2.2.2):** Słabe wymagania hasła - Admin wymaga tylko 6 znaków bez complexity
3. **SEC-003 (14.5.3):** CORS AllowAnyOrigin w produkcji
4. **SEC-004 (14.4.3, 14.4.4, 14.4.6, 14.4.7):** Brak security headers (CSP, X-Frame-Options, X-Content-Type-Options, Referrer-Policy)
5. **SEC-008 (4.3.3, 8.3.2):** Anonymous customer endpoints expose/modify PII

### Rekomendacje wysokiego priorytetu

1. **Natychmiast:** Usunąć sekrety z repozytorium, rotować wszystkie klucze, przenieść do Azure Key Vault
2. **Do 24h:** Wzmocnić politykę haseł (min. 12 znaków z complexity)
3. **Do 48h:** Skonfigurować CORS z explicit allowlist domen
4. **Do tygodnia:** Dodać security headers middleware
5. **Do tygodnia:** Dodać rate limiting na API endpoints
6. **Do tygodnia:** Zabezpieczyć endpointy klientów (auth + ograniczenia dostępu do PII)
7. **Do miesiąca:** Wdrożyć MFA dla panelu administracyjnego

### Następne kroki

1. Przegląd raportu `ASVS_L2_RAPORT.md` z szczegółowymi rekomendacjami
2. Priorytetyzacja napraw według severity
3. Implementacja poprawek krytycznych (sekrety, hasła, CORS)
4. Re-audyt po naprawach

---

**Data ukończenia audytu:** 2026-04-04  
**Audytor:** Cascade AI Security Audit  
**Wersja aplikacji:** SportRental (commit aktualny)
