# 🏂 SportRental - Case Study

## Przegląd Projektu

**SportRental** to enterprise-grade, multi-tenant platforma do wypożyczania sprzętu sportowego. System został zaprojektowany jako kompleksowe rozwiązanie SaaS dla wypożyczalni nart, rowerów, sprzętu wodnego i innych urządzeń sportowych.

### Kluczowe Metryki Projektu

| Metryka | Wartość |
|---------|---------|
| **Stack technologiczny** | .NET 10, Blazor Server + WASM, PostgreSQL |
| **Liczba projektów w solucji** | 11 projektów |
| **Testy automatyczne** | 356 testów (100% passing) |
| **Linie kodu** | ~50,000+ LOC |
| **Status** | Production Ready |

---

## 🎯 Problem Biznesowy

Wypożyczalnie sprzętu sportowego potrzebują:
- **Centralnego systemu zarządzania** - produkty, klienci, wypożyczenia
- **Obsługi płatności online** - z depozytami i kaucjami
- **Automatyzacji dokumentów** - umowy, potwierdzenia
- **Multi-lokalizacji** - jedna platforma dla wielu wypożyczalni
- **Responsywnego UI** - dostęp z każdego urządzenia

### Rozwiązanie

Zaprojektowałem i zaimplementowałem pełnoprawną platformę SaaS z:
- **Architekturą multi-tenant** - izolacja danych per wypożyczalnia
- **Dwoma interfejsami** - panel administracyjny (Blazor Server) + aplikacja kliencka (Blazor WASM)
- **Integracją Stripe** - płatności online z obsługą depozytów
- **Automatyczną generacją dokumentów** - umowy PDF z QuestPDF
- **Powiadomieniami** - email SMTP + SMS (SMSAPI.pl)

---

## 🏗️ Architektura Systemu

### Diagram Architektury

```
┌─────────────────────────────────────────────────────────────────┐
│                        FRONTEND LAYER                           │
├─────────────────────────────────┬───────────────────────────────┤
│     Blazor WASM (Client)        │    Blazor Server (Admin)      │
│  • Katalog produktów            │  • Zarządzanie produktami     │
│  • Koszyk + Checkout            │  • Obsługa wypożyczeń         │
│  • Konto klienta                │  • Zarządzanie klientami      │
│  • Mapa lokalizacji             │  • Raporty i statystyki       │
│  • TailwindCSS + Mobile-First   │  • MudBlazor + Dark Mode      │
└─────────────────────────────────┴───────────────────────────────┘
                              │
                              │ REST API + X-Tenant-Id
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                        BACKEND LAYER                            │
│                    SportRental.Admin (API Host)                 │
├─────────────────────────────────────────────────────────────────┤
│  Services:                       │  Integrations:               │
│  • ContractGenerator (PDF)       │  • Stripe (Payments)         │
│  • EmailSender (SMTP)            │  • Azure Key Vault (Secrets) │
│  • SmsSender (SMSAPI.pl)         │  • Azure Blob Storage (Files)│
│  • PaymentCalculator             │  • SignalR (Real-time)       │
│  • HoldService (Reservations)    │                              │
└─────────────────────────────────────────────────────────────────┘
                              │
                              │ Entity Framework Core 10
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                         DATA LAYER                              │
├─────────────────────────────────────────────────────────────────┤
│                      PostgreSQL 14+                             │
│  • Multi-tenant Query Filters                                   │
│  • 20+ encji domenowych                                         │
│  • Indeksy na tenant + timestamp                                │
│  • Migracje EF Core                                             │
└─────────────────────────────────────────────────────────────────┘
```

### Struktura Projektów

```
SportRentalHybrid.sln
├── SportRental.Admin/           # Blazor Server + API Host
│   ├── Api/                     # REST Endpoints (Minimal API)
│   ├── Components/              # UI Components (MudBlazor)
│   ├── Services/                # Business Logic
│   │   ├── Contracts/           # PDF Generation (QuestPDF)
│   │   ├── Email/               # SMTP Notifications
│   │   ├── Sms/                 # SMS Integration
│   │   └── Storage/             # Azure Blob Storage
│   ├── Payments/                # Stripe Integration
│   └── Hubs/                    # SignalR Hubs
│
├── SportRental.Client/          # Blazor WebAssembly
│   ├── Pages/                   # Public Pages
│   ├── Components/              # Reusable UI
│   ├── Services/                # API Clients
│   └── wwwroot/                 # TailwindCSS Assets
│
├── SportRental.Infrastructure/  # Data Layer
│   ├── Domain/                  # Entity Models
│   ├── Migrations/              # EF Core Migrations
│   └── Tenancy/                 # Multi-tenant Logic
│
├── SportRental.Shared/          # Shared Library
│   ├── Models/                  # DTOs
│   ├── Services/                # Shared HTTP Clients
│   └── Components/              # Shared UI Components
│
├── SportRental.Api/             # (Prepared for future scaling)
├── SportRental.MediaStorage/    # (Prepared for self-hosted files)
│
└── Tests/
    ├── SportRental.Admin.Tests/     # 301 tests
    ├── SportRental.Api.Tests/       # 30 tests
    ├── SportRental.Client.Tests/    # 19 tests
    └── SportRental.E2ETests/        # End-to-end tests
```

---

## 💡 Kluczowe Decyzje Architektoniczne

### 1. Multi-Tenant z Query Filters

Zaimplementowałem izolację danych na poziomie EF Core używając Global Query Filters:

```csharp
public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    private Guid? _tenantId;

    public void SetTenant(Guid? tenantId) => _tenantId = tenantId;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Automatyczna filtracja po tenancie
        modelBuilder.Entity<Product>()
            .HasQueryFilter(p => _tenantId == null || p.TenantId == _tenantId);
        
        modelBuilder.Entity<Rental>()
            .HasQueryFilter(r => _tenantId == null || r.TenantId == _tenantId);
        
        // ... pozostałe encje
    }
}
```

**Korzyści:**
- Automatyczna izolacja danych bez modyfikacji zapytań
- Zabezpieczenie przed wyciekiem danych między tenantami
- Łatwe testowanie (wyłączenie filtra w testach)

### 2. Hybrid Blazor (Server + WASM)

Wybrałem architekturę hybrydową:

| Komponent | Technologia | Uzasadnienie |
|-----------|-------------|--------------|
| **Admin Panel** | Blazor Server | Bezpośredni dostęp do bazy, brak opóźnień WASM, autentykacja Identity |
| **Public Client** | Blazor WASM | Statyczny hosting (Azure Static Web Apps), offline-capable, CDN |
| **Shared Library** | Razor Class Library | Współdzielenie DTOs, komponentów, serwisów HTTP |

### 3. Azure Key Vault dla Sekretów

**Zero hardcoded secrets** - wszystkie wrażliwe dane w Azure Key Vault:

```csharp
// Program.cs
builder.Configuration.AddAzureKeyVault(
    new Uri(keyVaultUrl),
    new DefaultAzureCredential());

// Sekrety pobierane automatycznie:
// - ConnectionStrings--DefaultConnection
// - Stripe--SecretKey
// - Email--Smtp--Password
// - Storage--AzureBlob--ConnectionString
```

### 4. Dual UI Strategy (Mobile-First)

Zaimplementowałem osobne widoki dla mobile i desktop:

```razor
@if (_isMobile)
{
    <!-- Mobile: Karty produktów, sticky headers -->
    <div class="grid grid-cols-2 gap-2">
        @foreach (var product in _products)
        {
            <ProductCard Product="product" />
        }
    </div>
}
else
{
    <!-- Desktop: Tabele z pełnymi danymi -->
    <MudTable Items="_products" ...>
}
```

**JavaScript Interop dla detekcji:**
```javascript
window.setupMobileDetection = function(dotNetRef) {
    const checkMobile = () => window.innerWidth < 768;
    window.addEventListener('resize', () => 
        dotNetRef.invokeMethodAsync('OnScreenResize', checkMobile())
    );
};
```

---

## 🔧 Kluczowe Funkcjonalności

### 1. System Wypożyczeń

**Model domeny:**

```csharp
public class Rental
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid CustomerId { get; set; }
    
    public DateTime StartDateUtc { get; set; }
    public DateTime EndDateUtc { get; set; }
    
    public RentalStatus Status { get; set; }  // Draft → Pending → Confirmed → Active → Completed
    public RentalType RentalType { get; set; } // Daily / Hourly
    public RentalSource Source { get; set; }   // Online / InStore
    
    public decimal TotalAmount { get; set; }
    public decimal DepositAmount { get; set; }
    public string? PaymentIntentId { get; set; }
    
    // Tracking wydania/zwrotu
    public DateTime? IssuedAtUtc { get; set; }
    public DateTime? ReturnedAtUtc { get; set; }
    public decimal? DamageCharge { get; set; }
    
    public List<RentalItem> Items { get; set; }
}
```

**Flow wypożyczenia:**
1. Klient dodaje produkty do koszyka → tworzone są `ReservationHold` (TTL 20 min)
2. Przejście do checkout → kalkulacja ceny i depozytu
3. Płatność przez Stripe Checkout Sessions
4. Webhook Stripe → potwierdzenie → generacja umowy PDF → email do klienta
5. Wydanie sprzętu (Admin) → status `Active`
6. Zwrot sprzętu → rozliczenie depozytu

### 2. Integracja Stripe

**Implementacja Payment Gateway:**

```csharp
public sealed class StripePaymentGateway : IPaymentGateway
{
    public async Task<PaymentIntentDto> CreatePaymentIntentAsync(
        Guid tenantId, 
        decimal amount, 
        decimal depositAmount, 
        string currency,
        Dictionary<string, string>? metadata = null)
    {
        var createOptions = new PaymentIntentCreateOptions
        {
            Amount = (long)(amount * 100), // Stripe = grosze/centy
            Currency = currency.ToLowerInvariant(),
            AutomaticPaymentMethods = new() { Enabled = true },
            Metadata = new Dictionary<string, string>
            {
                ["tenant_id"] = tenantId.ToString(),
                ["deposit_amount"] = ((long)(depositAmount * 100)).ToString()
            }
        };
        
        var paymentIntent = await _paymentIntentService.CreateAsync(createOptions);
        return MapToDto(paymentIntent, depositAmount);
    }
}
```

**Obsługiwane operacje:**
- Tworzenie Payment Intent z metadanymi tenanta
- Capture / Cancel / Refund z weryfikacją własności
- Webhook handling dla asynchronicznych potwierdzeń

### 3. Generacja Umów PDF (QuestPDF)

**Profesjonalne umowy wypożyczenia:**

```csharp
public class QuestPdfContractGenerator : IContractGenerator
{
    public Task<byte[]> GenerateRentalContractAsync(
        Rental rental, 
        IEnumerable<RentalItem> items, 
        Customer customer, 
        IEnumerable<Product> products,
        CompanyInfo? companyInfo = null)
    {
        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(1.5f, Unit.Centimetre);
                
                // Header z danymi firmy
                page.Header().Column(col => { ... });
                
                // Treść: strony umowy, przedmiot, warunki
                page.Content().Column(col =>
                {
                    // §1. STRONY UMOWY
                    // §2. PRZEDMIOT WYPOŻYCZENIA (tabela produktów)
                    // §3. WARUNKI FINANSOWE
                    // §4. OBOWIĄZKI STRON
                    // §5. POSTANOWIENIA KOŃCOWE
                });
                
                // Footer z podpisami
                page.Footer().Row(row => { ... });
            });
        });
        
        return Task.FromResult(doc.GeneratePdf());
    }
}
```

### 4. System Powiadomień

**Email (SMTP):**
```csharp
public class SmtpEmailSender : IEmailSender
{
    public async Task SendRentalConfirmationAsync(
        Rental rental, 
        Customer customer, 
        byte[] contractPdf)
    {
        var message = new MimeMessage();
        message.To.Add(new MailboxAddress(customer.FullName, customer.Email));
        message.Subject = $"Potwierdzenie wypożyczenia #{rental.Id:N}";
        
        // HTML body + załącznik PDF
        var builder = new BodyBuilder();
        builder.HtmlBody = await RenderEmailTemplate(rental, customer);
        builder.Attachments.Add($"Umowa_{rental.Id}.pdf", contractPdf);
        
        message.Body = builder.ToMessageBody();
        await _smtpClient.SendAsync(message);
    }
}
```

**SMS (SMSAPI.pl / SerwerSMS):**
```csharp
public class SmsApiSender : ISmsSender
{
    public async Task SendConfirmationSmsAsync(
        string phoneNumber, 
        string confirmationCode,
        Guid rentalId)
    {
        var message = $"Twoje wypożyczenie zostało potwierdzone. " +
                      $"Kod odbioru: {confirmationCode}";
        
        await _httpClient.PostAsJsonAsync("/sms/send", new
        {
            to = phoneNumber,
            message = message,
            sender = "SportRental"
        });
    }
}
```

### 5. Reservation Holds (Tymczasowe Rezerwacje)

**System holdów w koszyku:**

```csharp
public class ReservationHold
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProductId { get; set; }
    public int Quantity { get; set; }
    public DateTime StartDateUtc { get; set; }
    public DateTime EndDateUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }  // TTL
    public string? SessionId { get; set; }
}
```

**Endpoint API:**
```csharp
app.MapPost("/api/holds", async (CreateHoldRequest req, ApplicationDbContext db) =>
{
    var hold = new ReservationHold
    {
        ProductId = req.ProductId,
        Quantity = req.Quantity,
        StartDateUtc = req.StartDateUtc,
        EndDateUtc = req.EndDateUtc,
        ExpiresAtUtc = DateTime.UtcNow.AddMinutes(req.TtlMinutes ?? 20)
    };
    
    db.ReservationHolds.Add(hold);
    await db.SaveChangesAsync();
    
    return Results.Ok(new CreateHoldResponse(hold.Id, hold.ExpiresAtUtc));
});
```

### 6. Mapa Lokalizacji (Leaflet)

**Interaktywna mapa wypożyczalni:**
- Widok mapy z markerami wszystkich lokalizacji
- Filtrowanie po województwie/mieście
- Popup z danymi kontaktowymi i godzinami otwarcia
- Integracja przez JS Interop

---

## 🔐 Bezpieczeństwo

### Implementacja

| Aspekt | Rozwiązanie |
|--------|-------------|
| **Sekrety** | Azure Key Vault (zero w kodzie) |
| **Autentykacja** | ASP.NET Core Identity + Cookie Auth |
| **Autoryzacja** | Role-based (SuperAdmin, Owner, Employee, Client) |
| **Multi-tenancy** | Header `X-Tenant-Id` + Query Filters |
| **HTTPS** | Wymuszony w produkcji |
| **Płatności** | Stripe Checkout (PCI-compliant) |

### Role i Uprawnienia

```csharp
public class EmployeePermissions
{
    // Produkty
    public bool CanViewProducts { get; set; }
    public bool CanManageProducts { get; set; }
    
    // Wypożyczenia
    public bool CanViewRentals { get; set; }
    public bool CanManageRentals { get; set; }
    public bool CanIssueEquipment { get; set; }
    public bool CanReturnEquipment { get; set; }
    
    // Klienci
    public bool CanViewCustomers { get; set; }
    public bool CanManageCustomers { get; set; }
    
    // Finanse
    public bool CanViewFinances { get; set; }
    public bool CanProcessPayments { get; set; }
}
```

---

## 🧪 Testowanie

### Strategia Testów

```
356 testów automatycznych
├── Unit Tests (Services, Validators)
├── Integration Tests (API Endpoints, Database)
├── Component Tests (Blazor bUnit)
└── E2E Tests (WebApplicationFactory)
```

### Przykład Testu Integracyjnego

```csharp
public class RentalsEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task CreateRental_WithValidData_ReturnsConfirmedRental()
    {
        // Arrange
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Id", _testTenantId.ToString());
        
        var request = new CreateRentalRequest
        {
            CustomerId = _testCustomerId,
            StartDateUtc = DateTime.UtcNow.AddDays(1),
            EndDateUtc = DateTime.UtcNow.AddDays(3),
            Items = new[] { new RentalItemRequest(_productId, 1) },
            PaymentIntentId = "pi_test_123"
        };
        
        // Act
        var response = await client.PostAsJsonAsync("/api/rentals", request);
        
        // Assert
        response.Should().HaveStatusCode(HttpStatusCode.OK);
        var rental = await response.Content.ReadFromJsonAsync<RentalResponse>();
        rental.Status.Should().Be(RentalStatus.Confirmed);
        rental.TotalAmount.Should().BeGreaterThan(0);
    }
}
```

---

## 📦 Deployment

### Infrastruktura Azure

```
┌─────────────────────────────────────────────────────────────┐
│                     AZURE CLOUD                              │
├────────────────────────┬────────────────────────────────────┤
│  Azure App Service     │  Azure Static Web Apps             │
│  (SportRental.Admin)   │  (SportRental.Client)              │
│  • Blazor Server       │  • Blazor WASM                     │
│  • REST API            │  • CDN Distribution                │
├────────────────────────┼────────────────────────────────────┤
│  Azure Key Vault       │  Azure Blob Storage                │
│  • Connection Strings  │  • Product Images                  │
│  • API Keys            │  • Generated PDFs                  │
│  • JWT Secrets         │  • Company Logos                   │
├────────────────────────┴────────────────────────────────────┤
│                    PostgreSQL (Managed)                      │
└─────────────────────────────────────────────────────────────┘
```

### Deploy Commands

```powershell
# Admin Panel (App Service)
dotnet publish SportRental.Admin -c Release -o ./publish/admin
az webapp deployment source config-zip `
    --resource-group DefaultResourceGroup-PLC `
    --name sradmin2 `
    --src ./publish/admin.zip

# Client WASM (Static Web Apps)
dotnet publish SportRental.Client -c Release -o ./publish/client
swa deploy ./publish/client/wwwroot `
    --deployment-token $env:SWA_TOKEN `
    --env production
```

---

## 📊 Tech Stack Summary

### Backend
- **.NET 10** - najnowsza wersja platformy
- **C# 12** - pattern matching, primary constructors
- **Entity Framework Core 10** - ORM z Query Filters
- **ASP.NET Core Identity** - autentykacja i autoryzacja
- **Minimal APIs** - lekkie REST endpoints
- **SignalR** - real-time updates

### Frontend
- **Blazor Server** - panel administracyjny
- **Blazor WebAssembly** - aplikacja kliencka
- **MudBlazor** - komponenty Material Design
- **TailwindCSS** - utility-first styling
- **Leaflet.js** - mapy interaktywne

### Cloud & DevOps
- **Azure App Service** - hosting backend
- **Azure Static Web Apps** - hosting WASM
- **Azure Key Vault** - zarządzanie sekretami
- **Azure Blob Storage** - przechowywanie plików
- **GitHub** - kontrola wersji

### Integracje
- **Stripe** - płatności online
- **QuestPDF** - generacja dokumentów PDF
- **SMSAPI.pl / SerwerSMS** - powiadomienia SMS
- **SMTP** - powiadomienia email

---

## 🎨 Zaawansowane Komponenty UI

### QR Scanner - Skanowanie Kodów w Przeglądarce

Zaimplementowałem komponent do skanowania kodów QR bezpośrednio z kamery urządzenia:

```razor
<div class="qr-scanner-container">
    @if (_isScanning)
    {
        <div id="@_scannerId" class="qr-scanner-video"></div>
        <div class="qr-scanner-controls">
            <MudButton OnClick="ToggleFlash" StartIcon="@Icons.Material.Filled.FlashOn">
                Latarka
            </MudButton>
            <MudButton OnClick="StopScanning" Color="Color.Error">
                Stop
            </MudButton>
        </div>
    }
</div>
```

**Funkcjonalności:**
- Dostęp do kamery przez WebRTC
- Obsługa latarki (torch) na urządzeniach mobilnych
- Automatyczne rozpoznawanie produktów po zeskanowaniu
- Integracja z html5-qrcode library

### QR Label Generator - Drukowanie Etykiet

Generator etykiet z kodami QR do naklejenia na sprzęt:

```csharp
public class QrLabelGenerator : IQrLabelGenerator
{
    public async Task<byte[]> GenerateLabelsAsync(
        IEnumerable<(Product Product, int Quantity)> products, 
        LabelSize labelSize = LabelSize.Medium)
    {
        // Trzy rozmiary: Small (30x30mm), Medium (50x50mm), Large (70x70mm)
        var (labelWidth, labelHeight, columns, rows, qrSize, fontSize) = 
            GetLabelDimensions(labelSize);
        
        // QuestPDF generuje PDF z siatką etykiet
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Content().Table(table =>
                {
                    // Siatka etykiet: nazwa produktu + kod QR + SKU
                });
            });
        }).GeneratePdf();
    }
}
```

**Formaty etykiet:**
- **Small** - 45 etykiet/strona (5×9)
- **Medium** - 15 etykiet/strona (3×5)
- **Large** - 8 etykiet/strona (2×4)

### Interaktywna Mapa Lokalizacji (Leaflet)

Mapa wypożyczalni z filtrowaniem po odległości:

```razor
@page "/map"
<MudContainer>
    <!-- Kontrolki lokalizacji -->
    <MudSelect T="int" @bind-Value="selectedRadius" Label="Promień wyszukiwania">
        <MudSelectItem Value="0">Pokaż wszystkie</MudSelectItem>
        <MudSelectItem Value="5">Do 5 km</MudSelectItem>
        <MudSelectItem Value="10">Do 10 km</MudSelectItem>
        <MudSelectItem Value="25">Do 25 km</MudSelectItem>
    </MudSelect>
    
    <MudButton OnClick="RequestGeolocation" StartIcon="@Icons.Material.Filled.GpsFixed">
        Użyj mojej lokalizacji
    </MudButton>
    
    <!-- Mapa Leaflet -->
    <div id="map-container"></div>
    
    <!-- Lista wypożyczalni z odległością -->
    @foreach (var location in filteredLocations)
    {
        <LocationCard Location="location" Distance="@CalculateDistance(location)" />
    }
</MudContainer>
```

**Funkcje mapy:**
- Geolokalizacja użytkownika (GPS)
- Kliknięcie na mapie ustawia lokalizację
- Filtrowanie po promieniu (5-100 km)
- Markery z popup (dane kontaktowe, godziny otwarcia)
- Obliczanie odległości (Haversine formula)

---

## 🔄 Real-Time Updates (SignalR)

### Architektura Powiadomień

System używa SignalR do real-time aktualizacji statusów wypożyczeń:

```csharp
public class RentalNotificationHub : Hub
{
    // Klienci dołączają do grupy swojego tenanta
    public async Task JoinTenantGroup(string tenantId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"tenant_{tenantId}");
    }
}

// DTO dla eventów
public record RentalStatusChangedEvent(
    Guid RentalId,
    string NewStatus,
    bool IsSmsConfirmed,
    bool IsSmsConfirmationSent,
    DateTime ChangedAtUtc
);
```

**Scenariusze użycia:**
- Klient potwierdza wynajem SMS-em → panel admina aktualizuje się automatycznie
- Admin zmienia status → klient widzi zmianę bez odświeżania
- Nowe wypożyczenie online → notyfikacja w dashboardzie

---

## 🛒 Smart Cart z Reservation Holds

### Zaawansowany System Koszyka

Koszyk z automatycznym zarządzaniem rezerwacjami (holds):

```csharp
public class CartService : ICartService
{
    private const string CART_KEY = "sport-rental-cart";
    
    public async Task AddToCartAsync(ProductDto product, int quantity, 
        DateTime? startDate, DateTime? endDate)
    {
        _cart.AddItem(product, quantity, startDate, endDate);
        await SaveCartToStorageAsync();
        
        // Automatyczne tworzenie holda gdy są daty
        if (startDate.HasValue && endDate.HasValue)
        {
            _ = EnsureHoldsAsync();  // Fire-and-forget
        }
    }
    
    public async Task RefreshHoldsIfNeededAsync(TimeSpan? beforeExpiry = null)
    {
        var threshold = beforeExpiry ?? TimeSpan.FromMinutes(2);
        
        foreach (var item in _cart.Items)
        {
            if (item.HoldExpiresAtUtc - DateTime.UtcNow <= threshold)
            {
                // Odnów hold przed wygaśnięciem
                var newHold = await _apiService.CreateHoldAsync(...);
                await _apiService.DeleteHoldAsync(item.HoldId.Value);
                item.HoldId = newHold.Id;
            }
        }
    }
}
```

**Mechanizmy koszyka:**
- **Persystencja** - localStorage w przeglądarce
- **Holds** - tymczasowe rezerwacje (TTL 10 min)
- **Auto-refresh** - odnawianie holdów przed wygaśnięciem
- **Walidacja** - sprawdzanie dostępności przed checkout
- **Cleanup** - zwalnianie holdów przy usuwaniu z koszyka

---

## 📱 SMS Confirmation Flow

### Dwuetapowe Potwierdzenie Wypożyczenia

System potwierdzania wypożyczeń przez SMS:

```csharp
public class SmsConfirmationService : ISmsConfirmationService
{
    // Słowa kluczowe akceptowane jako potwierdzenie
    private static readonly string[] ConfirmationKeywords = 
        { "TAK", "YES", "OK", "POTWIERDZAM", "1" };
    
    public async Task<string> GenerateConfirmationCodeAsync(Guid rentalId)
    {
        var code = Random.Shared.Next(100000, 999999).ToString();
        
        var confirmation = new SmsConfirmation
        {
            RentalId = rentalId,
            Code = code,
            PhoneNumber = rental.Customer.PhoneNumber,
            ExpiresAt = DateTime.UtcNow.AddHours(24)
        };
        
        await context.SmsConfirmations.Add(confirmation);
        return code;
    }
    
    public async Task<bool> ProcessIncomingSmsAsync(string phoneNumber, string message)
    {
        // Rozpoznaj odpowiedź klienta
        var normalized = message.Trim().ToUpperInvariant();
        
        if (ConfirmationKeywords.Any(k => normalized.Contains(k)))
        {
            // Potwierdź wynajem
            rental.IsSmsConfirmed = true;
            rental.Status = RentalStatus.Confirmed;
            
            // Powiadom panel admina przez SignalR
            await _notificationService.NotifyRentalStatusChangedAsync(...);
        }
    }
}
```

**Flow SMS:**
1. Klient składa zamówienie online
2. System wysyła SMS: *"Wypożyczenie #ABC123. Odpowiedz TAK aby potwierdzić."*
3. Klient odpowiada "TAK"
4. Webhook SMSAPI.pl → `ProcessIncomingSmsAsync`
5. Status zmienia się na `Confirmed`
6. Panel admina aktualizuje się przez SignalR
7. Generowana jest umowa PDF

---

## 🔗 Short Links dla Umów

### Przyjazne URL-e do Umów Wypożyczenia

System skróconych linków do podglądu umów:

```csharp
// GET /c/{shortId} - np. /c/ABC12345
app.MapGet("/c/{shortId}", async (string shortId, IDbContextFactory<ApplicationDbContext> dbFactory) =>
{
    await using var db = await dbFactory.CreateDbContextAsync();
    
    // Wyszukaj po pierwszych 8 znakach GUID
    var rental = await db.Rentals
        .IgnoreQueryFilters()  // Bez filtra tenanta - publiczny dostęp
        .Where(r => r.Id.ToString().StartsWith(shortId.ToLower()))
        .FirstOrDefaultAsync();
    
    if (rental?.ContractUrl != null)
    {
        return Results.Redirect(rental.ContractUrl);  // PDF w Azure Blob
    }
    
    // Fallback: generuj stronę HTML z podstawowymi danymi
    return Results.Content(GenerateContractHtmlPage(rental), "text/html");
});
```

**Zastosowania:**
- SMS do klienta: *"Twoja umowa: sportrental.pl/c/ABC12345"*
- Kod QR na wydrukowanej umowie
- Łatwe udostępnianie linku

---

## 📊 Admin Dashboard

### Widżety Dashboardu

Interaktywny dashboard z kluczowymi metrykami:

```razor
<MudGrid>
    <!-- Karty statystyk -->
    <MudItem xs="12" sm="6" md="3">
        <StatCard Title="Dziś do wydania" Value="@todayIssues" 
                  Icon="@Icons.Material.Filled.PlayArrow" Color="Color.Primary" />
    </MudItem>
    <MudItem xs="12" sm="6" md="3">
        <StatCard Title="Dziś do zwrotu" Value="@todayReturns" 
                  Icon="@Icons.Material.Filled.AssignmentReturn" Color="Color.Warning" />
    </MudItem>
    <MudItem xs="12" sm="6" md="3">
        <StatCard Title="Aktywne wynajmy" Value="@activeRentals" 
                  Icon="@Icons.Material.Filled.TrendingUp" Color="Color.Success" />
    </MudItem>
    <MudItem xs="12" sm="6" md="3">
        <StatCard Title="Klientów" Value="@customersCount" 
                  Icon="@Icons.Material.Filled.People" Color="Color.Info" />
    </MudItem>
    
    <!-- Szybkie akcje -->
    <MudItem xs="12" lg="4">
        <QuickActionsCard>
            <MudButton Href="/admin/rentals">Utwórz wynajem</MudButton>
            <MudButton Href="/admin/products">Zarządzaj produktami</MudButton>
            <MudButton Href="/admin/schedule">Kalendarz</MudButton>
        </QuickActionsCard>
    </MudItem>
    
    <!-- Ostatnie wydarzenia -->
    <MudItem xs="12" lg="8">
        <RecentActivityFeed Activities="@recentActivities" />
    </MudItem>
</MudGrid>
```

---

## 🔍 Zaawansowane Filtrowanie Produktów

### API z Wieloma Parametrami Filtrowania

Endpoint produktów z rozbudowanym filtrowaniem:

```csharp
app.MapGet("/api/products", async (
    int? page, int? pageSize,
    string? search,           // Wyszukiwanie tekstowe
    string? category,         // Filtr kategorii
    string? city,             // Filtr miasta
    string? voivodeship,      // Filtr województwa
    string? tenant,           // Filtr wypożyczalni
    decimal? minPrice,        // Cena minimalna
    decimal? maxPrice,        // Cena maksymalna
    bool? available,          // Tylko dostępne
    string? sort,             // Sortowanie
    double? userLat,          // Lokalizacja użytkownika (dla sortowania)
    double? userLon) =>
{
    var query = db.Products
        .Join(db.Tenants, ...)
        .GroupJoin(db.CompanyInfos, ...);  // Dane firmy dla lokalizacji
    
    // Dynamiczne budowanie zapytania
    if (!string.IsNullOrWhiteSpace(search))
        query = query.Where(p => p.Name.Contains(search) || p.Description.Contains(search));
    
    if (!string.IsNullOrWhiteSpace(category))
        query = query.Where(p => p.Category == category);
    
    // Sortowanie po odległości gdy mamy lokalizację użytkownika
    if (userLat.HasValue && userLon.HasValue)
        query = query.OrderBy(p => CalculateDistance(userLat, userLon, p.Lat, p.Lon));
    
    return await query.ToListAsync();
});
```

**Obsługiwane filtry:**
- **Tekstowe** - nazwa, opis
- **Kategoryczne** - kategoria, miasto, województwo, wypożyczalnia
- **Numeryczne** - zakres cen
- **Geograficzne** - sortowanie po odległości od użytkownika
- **Paginacja** - page, pageSize

---

## 🚀 Wnioski

### Osiągnięcia Projektu

1. **Kompletne rozwiązanie SaaS** - od frontendu po deployment
2. **Skalowalna architektura** - multi-tenant, przygotowana na microservices
3. **Enterprise-grade security** - Azure Key Vault, zero secrets w kodzie
4. **Production-ready** - 356 testów, dokumentacja, CI/CD ready
5. **Modern tech stack** - .NET 10, Blazor, TailwindCSS

### Umiejętności Zademonstrowane

- **Projektowanie architektury** - multi-tenant, clean architecture
- **Full-stack development** - backend + frontend + database
- **Integracje zewnętrzne** - Stripe, Azure services, SMS gateways
- **DevOps** - Azure deployment, configuration management
- **Testowanie** - unit, integration, E2E tests
- **UI/UX** - responsive design, mobile-first approach

---

## 📞 Kontakt

**Damian Tarnowski**  
📧 hdtdtr@gmail.com  
💼 [GitHub](https://github.com/DamianTarnowski)

---

*Projekt rozwijany od 2024 roku. Ostatnia aktualizacja: Grudzień 2025*
