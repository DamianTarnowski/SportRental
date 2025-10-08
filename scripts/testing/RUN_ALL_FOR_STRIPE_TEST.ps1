# 🚀 SportRental - Uruchom wszystko dla testów Stripe
# Otwiera 3 terminale z API, Client i Stripe webhooks

Write-Host "🚀 Uruchamianie SportRental dla testów Stripe..." -ForegroundColor Green
Write-Host ""

# Check if stripe.exe exists
if (!(Test-Path "stripe.exe")) {
    Write-Host "❌ stripe.exe nie znaleziony!" -ForegroundColor Red
    Write-Host "   Pobierz z: https://github.com/stripe/stripe-cli/releases" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "⚠️  Możesz uruchomić bez webhooków (tylko API + Client):" -ForegroundColor Yellow
    Write-Host "   1. Terminal 1: dotnet run --project SportRental.Api" -ForegroundColor Gray
    Write-Host "   2. Terminal 2: dotnet run --project SportRental.Client" -ForegroundColor Gray
    exit 1
}

Write-Host "📋 Plan uruchomienia:" -ForegroundColor Cyan
Write-Host "  1️⃣  Terminal 1: Backend API (https://localhost:7142)" -ForegroundColor Gray
Write-Host "  2️⃣  Terminal 2: Frontend Client (http://localhost:5014)" -ForegroundColor Gray
Write-Host "  3️⃣  Terminal 3: Stripe Webhooks" -ForegroundColor Gray
Write-Host ""

# Terminal 1: API
Write-Host "1️⃣  Uruchamiam Backend API..." -ForegroundColor Yellow
Start-Process pwsh -ArgumentList "-NoExit", "-Command", "cd '$PWD'; Write-Host '🔧 Backend API' -ForegroundColor Cyan; dotnet run --project SportRental.Api"

Start-Sleep -Seconds 2

# Terminal 2: Client
Write-Host "2️⃣  Uruchamiam Frontend Client..." -ForegroundColor Yellow
Start-Process pwsh -ArgumentList "-NoExit", "-Command", "cd '$PWD'; Write-Host '🎨 Frontend Client' -ForegroundColor Magenta; dotnet run --project SportRental.Client"

Start-Sleep -Seconds 2

# Terminal 3: Stripe
Write-Host "3️⃣  Uruchamiam Stripe Webhooks..." -ForegroundColor Yellow
Start-Process pwsh -ArgumentList "-NoExit", "-Command", "cd '$PWD'; Write-Host '💳 Stripe Webhooks' -ForegroundColor Green; .\start-stripe-webhooks.ps1"

Write-Host ""
Write-Host "✅ Wszystkie terminale uruchomione!" -ForegroundColor Green
Write-Host ""
Write-Host "📋 Co dalej:" -ForegroundColor Cyan
Write-Host "  1. Poczekaj aż wszystko się załaduje (~30s)" -ForegroundColor Gray
Write-Host "  2. W terminalu Stripe skopiuj webhook secret (whsec_...)" -ForegroundColor Gray
Write-Host "  3. Wklej do SportRental.Api/appsettings.Development.json → Stripe:WebhookSecret" -ForegroundColor Gray
Write-Host "  4. Restart API (Terminal 1: Ctrl+C, potem dotnet run --project SportRental.Api)" -ForegroundColor Gray
Write-Host "  5. Otwórz przeglądarkę: http://localhost:5014" -ForegroundColor Gray
Write-Host "  6. Dodaj produkt, przejdź do checkout" -ForegroundColor Gray
Write-Host "  7. Użyj testowej karty: 4242 4242 4242 4242" -ForegroundColor Gray
Write-Host ""
Write-Host "💳 Testowe karty Stripe:" -ForegroundColor Yellow
Write-Host "  ✅ Sukces:    4242 4242 4242 4242" -ForegroundColor Green
Write-Host "  ❌ Odrzucona: 4000 0000 0000 0002" -ForegroundColor Red
Write-Host "  ⏳ 3D Secure: 4000 0025 0000 3155" -ForegroundColor Blue
Write-Host ""
Write-Host "📚 Dokumentacja: QUICK_START_STRIPE_TESTING.md" -ForegroundColor Cyan
Write-Host ""
Write-Host "🎉 Gotowe! Udanych testów!" -ForegroundColor Green
