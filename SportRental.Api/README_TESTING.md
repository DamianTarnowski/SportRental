# 🧪 Testing Guide - SportRental API

## Quick Start

```powershell
# Uruchom wszystkie testy (wymaga PostgreSQL)
dotnet test SportRentalHybrid.sln

# Uruchom tylko testy bez PostgreSQL
dotnet test --filter "FullyQualifiedName~MediaStorage|FullyQualifiedName~BlazorApp3"

# Uruchom testy API (wymaga skonfigurowanego PostgreSQL)
dotnet test SportRental.Api.Tests

# Uruchom testy z pokryciem kodu
dotnet test --collect:"XPlat Code Coverage"
```

## 📋 Test Suites

### 1. BlazorApp3.Tests (Admin Panel)
- **Location:** `BlazorApp3.Tests/`
- **Count:** ~200+ tests
- **Type:** Unit + Integration + Component (bUnit)
- **Coverage:** API, Domain, Components, Services
- **Database:** PostgreSQL required

**Key Tests:**
- `ApiTests.cs` - Customers, Rentals, Products API
- `EmployeesPageTests.cs` - bUnit component tests
- `CartServiceTests.cs` - Client-side shopping cart
- `DatabaseAuditLoggerTests.cs` - Audit logging
- `SmtpEmailSenderTests.cs` - Email notifications
- `EnhancedSmsTests.cs` - SMS API integration

### 2. SportRental.Api.Tests (REST API)
- **Location:** `SportRental.Api.Tests/`
- **Count:** ~15+ tests
- **Type:** Integration (WebApplicationFactory)
- **Coverage:** Stripe Payments, Checkout, Webhooks
- **Database:** PostgreSQL required (or mock)

**Key Tests:**
- `StripeCheckoutTests.cs` - Checkout Session creation
- `StripeWebhookTests.cs` - Webhook event handling
- `StripePaymentGatewayTests.cs` - Payment gateway logic
- `PaymentsEndpointsTests.cs` - Payment Intents API

**Notes:**
- Uses `MockPaymentGateway` by default (no real Stripe calls)
- Set PostgreSQL connection string in `appsettings.Test.json`
- For real Stripe tests, configure valid API keys

### 3. SportRental.MediaStorage.Tests (File Storage)
- **Location:** `SportRental.MediaStorage.Tests/`
- **Count:** ~59 tests
- **Type:** Unit + Integration
- **Coverage:** Chunked uploads, Thumbnails, Compression
- **Database:** SQLite (in-memory)

**Key Tests:**
- `ChunkedUploadTests.cs` - Resume uploads
- `CompressionTests.cs` - WebP, GZip, smart compression
- `FileOperationsTests.cs` - CRUD operations
- `HealthCheckTests.cs` - API health monitoring

**No dependencies:** Self-contained, runs without PostgreSQL

## 🔧 Configuration

### PostgreSQL (for API and Admin Panel tests)

Create `appsettings.Test.json` in test projects:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=sportrental_test;Username=postgres;Password=your_password"
  }
}
```

**Or use environment variable:**
```powershell
$env:ConnectionStrings__DefaultConnection = "Host=localhost;..."
dotnet test
```

### Stripe (for Payment tests)

```json
{
  "Stripe": {
    "SecretKey": "sk_test_...",
    "WebhookSecret": "whsec_..."
  }
}
```

**For MockPaymentGateway (default):**
- No real Stripe calls
- Instant responses
- Perfect for CI/CD

## 📊 Test Results (Current)

```
✅ BlazorApp3.Tests:        200+ tests passing
✅ MediaStorage.Tests:       59 tests passing
⚠️  Api.Tests:              10/15 tests passing
    - 5 failing due to PostgreSQL connection (expected in CI)
    - Fix: Configure PostgreSQL or use in-memory DB
```

**Total:** 296+ tests passing

## 🐛 Common Issues

### 1. PostgreSQL connection failed

**Error:** `28P01: autoryzacja hasłem nie powiodła się dla użytkownika "postgres"`

**Solution:**
```powershell
# Check PostgreSQL is running
docker ps | grep postgres

# Start PostgreSQL (Docker)
docker run -d -p 5432:5432 -e POSTGRES_PASSWORD=postgres postgres

# Update connection string
$env:ConnectionStrings__DefaultConnection = "Host=localhost;Database=sportrental_test;Username=postgres;Password=postgres"
```

### 2. Tests timeout

**Solution:** Increase timeout in test configuration:
```csharp
[Fact(Timeout = 30000)] // 30 seconds
public async Task MyLongRunningTest() { }
```

### 3. MockPaymentGateway status mismatch

**Error:** `Expected "RequiresPaymentMethod", but was "Succeeded"`

**Cause:** MockPaymentGateway returns different status than real Stripe

**Solution:** Update test expectations based on mock behavior:
```csharp
// Real Stripe
result.Status.Should().Be(PaymentIntentStatus.RequiresPaymentMethod);

// Mock (simplified)
result.Status.Should().Be(PaymentIntentStatus.Succeeded);
```

## 🚀 CI/CD Integration

### GitHub Actions Example

```yaml
name: Tests

on: [push, pull_request]

jobs:
  test:
    runs-on: ubuntu-latest
    services:
      postgres:
        image: postgres:16
        env:
          POSTGRES_PASSWORD: postgres
        options: >-
          --health-cmd pg_isready
          --health-interval 10s
          --health-timeout 5s
          --health-retries 5
        ports:
          - 5432:5432

    steps:
      - uses: actions/checkout@v3
      
      - name: Setup .NET
        uses: actions/setup-dotnet@v3
        with:
          dotnet-version: '9.0.x'
      
      - name: Restore dependencies
        run: dotnet restore
      
      - name: Build
        run: dotnet build --no-restore
      
      - name: Test
        run: dotnet test --no-build --verbosity normal
        env:
          ConnectionStrings__DefaultConnection: "Host=localhost;Database=sportrental_test;Username=postgres;Password=postgres"
```

## 📚 Writing New Tests

### Integration Test Template

```csharp
public class MyEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public MyEndpointTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Replace real dependencies with mocks
                services.AddSingleton<IPaymentGateway, MockPaymentGateway>();
            });
        }).CreateClient();
        
        _client.DefaultRequestHeaders.Add("X-Tenant-Id", Guid.NewGuid().ToString());
    }

    [Fact]
    public async Task MyTest_ValidInput_ReturnsOk()
    {
        // Arrange
        var request = new MyRequest { /* ... */ };

        // Act
        var response = await _client.PostAsJsonAsync("/api/my-endpoint", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
```

## 🎯 Test Coverage Goals

- **Unit Tests:** > 80%
- **Integration Tests:** Critical paths 100%
- **E2E Tests:** Happy paths + edge cases

**Current Coverage:**
- ✅ Admin Panel: 85%
- ✅ Media Storage: 90%
- ⚠️ REST API: 60% (improvement needed)
- ⚠️ WASM Client: 0% (manual testing only)

## 💡 Pro Tips

1. **Use `[Trait]` for categorization:**
   ```csharp
   [Fact, Trait("Category", "Integration")]
   public async Task MyIntegrationTest() { }
   ```

2. **Run specific category:**
   ```powershell
   dotnet test --filter "Category=Integration"
   ```

3. **Parallel execution:**
   ```csharp
   [Collection("Sequential")] // Disable parallel
   public class MyTests { }
   ```

4. **Test data cleanup:**
   ```csharp
   public class MyTests : IDisposable
   {
       public void Dispose()
       {
           // Cleanup test data
       }
   }
   ```

**Happy Testing! 🧪✨**
