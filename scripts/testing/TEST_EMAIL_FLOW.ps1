# TEST_EMAIL_FLOW.ps1
# Complete test script for email confirmation system

Write-Host "╔════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║  📧 EMAIL CONFIRMATION TEST FLOW 📧       ║" -ForegroundColor Cyan
Write-Host "╚════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

# Check if MailHog is running
Write-Host "🔍 Checking MailHog status..." -ForegroundColor Yellow
$mailhogRunning = $false
try {
    $response = Invoke-WebRequest -Uri "http://localhost:8025" -Method GET -TimeoutSec 2 -ErrorAction SilentlyContinue
    if ($response.StatusCode -eq 200) {
        $mailhogRunning = $true
        Write-Host "  ✅ MailHog is already running!" -ForegroundColor Green
    }
} catch {
    Write-Host "  ⚠️  MailHog not detected" -ForegroundColor Yellow
}

if (-not $mailhogRunning) {
    Write-Host ""
    Write-Host "📦 Starting MailHog (Docker)..." -ForegroundColor Yellow
    Write-Host "  Command: docker run -d -p 1025:1025 -p 8025:8025 mailhog/mailhog" -ForegroundColor Gray
    
    try {
        docker run -d -p 1025:1025 -p 8025:8025 mailhog/mailhog
        Write-Host "  ✅ MailHog started successfully!" -ForegroundColor Green
        Write-Host "  📬 Web UI: http://localhost:8025" -ForegroundColor Cyan
        Start-Sleep -Seconds 3
    } catch {
        Write-Host "  ❌ Failed to start MailHog. Make sure Docker is running." -ForegroundColor Red
        Write-Host "  Manual start: docker run -d -p 1025:1025 -p 8025:8025 mailhog/mailhog" -ForegroundColor Yellow
    }
}

Write-Host ""
Write-Host "═══════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""

# Build and run API
Write-Host "🔨 Building SportRental.Api..." -ForegroundColor Yellow
dotnet build SportRental.Api/SportRental.Api.csproj --verbosity quiet
if ($LASTEXITCODE -eq 0) {
    Write-Host "  ✅ Build successful!" -ForegroundColor Green
} else {
    Write-Host "  ❌ Build failed!" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "═══════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""

Write-Host "📝 TEST CHECKLIST:" -ForegroundColor Yellow
Write-Host ""
Write-Host "  1️⃣  MailHog Web UI:      http://localhost:8025" -ForegroundColor White
Write-Host "  2️⃣  API Swagger:         https://localhost:7142/swagger" -ForegroundColor White
Write-Host "  3️⃣  Client App:          http://localhost:5014" -ForegroundColor White
Write-Host "  4️⃣  Stripe Webhooks:     stripe listen --forward-to https://localhost:7142/api/webhooks/stripe" -ForegroundColor White
Write-Host ""
Write-Host "═══════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""

Write-Host "🚀 STEPS TO TEST:" -ForegroundColor Yellow
Write-Host ""
Write-Host "  Step 1: Open 3 terminals" -ForegroundColor Cyan
Write-Host "    Terminal 1: Run API" -ForegroundColor Gray
Write-Host "      dotnet run --project SportRental.Api" -ForegroundColor DarkGray
Write-Host ""
Write-Host "    Terminal 2: Run Client" -ForegroundColor Gray
Write-Host "      dotnet run --project SportRental.Client" -ForegroundColor DarkGray
Write-Host ""
Write-Host "    Terminal 3: Run Stripe CLI" -ForegroundColor Gray
Write-Host "      stripe listen --forward-to https://localhost:7142/api/webhooks/stripe" -ForegroundColor DarkGray
Write-Host ""

Write-Host "  Step 2: Make a test purchase" -ForegroundColor Cyan
Write-Host "    1. Navigate to http://localhost:5014" -ForegroundColor Gray
Write-Host "    2. Add products to cart" -ForegroundColor Gray
Write-Host "    3. Go to checkout" -ForegroundColor Gray
Write-Host "    4. Fill customer details" -ForegroundColor Gray
Write-Host "    5. Click 'Potwierdź i Zapłać'" -ForegroundColor Gray
Write-Host "    6. Use test card: 4242 4242 4242 4242" -ForegroundColor Gray
Write-Host "       - Date: 12/34" -ForegroundColor Gray
Write-Host "       - CVC: 123" -ForegroundColor Gray
Write-Host ""

Write-Host "  Step 3: Check email" -ForegroundColor Cyan
Write-Host "    1. Open http://localhost:8025" -ForegroundColor Gray
Write-Host "    2. Look for 'Potwierdzenie wypożyczenia' email" -ForegroundColor Gray
Write-Host "    3. Verify all details are correct" -ForegroundColor Gray
Write-Host ""

Write-Host "  Step 4: Verify in database" -ForegroundColor Cyan
Write-Host "    1. Check rental.Status = 'Confirmed'" -ForegroundColor Gray
Write-Host "    2. Check rental.PaymentStatus = 'Succeeded'" -ForegroundColor Gray
Write-Host "    3. Check rental.IsEmailSent = true" -ForegroundColor Gray
Write-Host ""

Write-Host "═══════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""

Write-Host "💳 STRIPE TEST CARDS:" -ForegroundColor Yellow
Write-Host ""
Write-Host "  ✅ Success:      4242 4242 4242 4242" -ForegroundColor Green
Write-Host "  ❌ Declined:     4000 0000 0000 0002" -ForegroundColor Red
Write-Host "  🔐 3D Secure:    4000 0025 0000 3155" -ForegroundColor Yellow
Write-Host ""
Write-Host "  Date: 12/34  |  CVC: 123" -ForegroundColor Gray
Write-Host ""

Write-Host "═══════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""

Write-Host "📧 EMAIL CONTENT CHECK:" -ForegroundColor Yellow
Write-Host ""
Write-Host "  Email should contain:" -ForegroundColor Cyan
Write-Host "    ✓ Gradient header (purple)" -ForegroundColor Gray
Write-Host "    ✓ Customer name" -ForegroundColor Gray
Write-Host "    ✓ Reservation number" -ForegroundColor Gray
Write-Host "    ✓ Start/End dates" -ForegroundColor Gray
Write-Host "    ✓ Number of days" -ForegroundColor Gray
Write-Host "    ✓ Product table with prices" -ForegroundColor Gray
Write-Host "    ✓ Total amount" -ForegroundColor Gray
Write-Host "    ✓ Deposit amount (30%)" -ForegroundColor Gray
Write-Host "    ✓ Remaining to pay" -ForegroundColor Gray
Write-Host "    ✓ Important reminders (yellow box)" -ForegroundColor Gray
Write-Host "    ✓ Contact information" -ForegroundColor Gray
Write-Host ""

Write-Host "═══════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""

Write-Host "📚 DOCUMENTATION:" -ForegroundColor Yellow
Write-Host ""
Write-Host "  Full guide: SportRental.Api/EMAIL_CONFIRMATIONS.md" -ForegroundColor Cyan
Write-Host ""

Write-Host "═══════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""

Write-Host "🎯 QUICK START (All-in-one):" -ForegroundColor Yellow
Write-Host ""
Write-Host "  Run this to start API, Client, and Webhooks:" -ForegroundColor Cyan
Write-Host "  .\RUN_ALL_FOR_STRIPE_TEST.ps1" -ForegroundColor White
Write-Host ""

Write-Host "╔════════════════════════════════════════════╗" -ForegroundColor Green
Write-Host "║         ✅ EMAIL SYSTEM READY! ✅          ║" -ForegroundColor Green
Write-Host "╚════════════════════════════════════════════╝" -ForegroundColor Green
Write-Host ""

# Ask if user wants to open MailHog
$openMailHog = Read-Host "Open MailHog Web UI in browser? (Y/N)"
if ($openMailHog -eq "Y" -or $openMailHog -eq "y") {
    Start-Process "http://localhost:8025"
    Write-Host "✅ Opened MailHog in browser!" -ForegroundColor Green
}

Write-Host ""
Write-Host "Happy testing! 🎉📧" -ForegroundColor Cyan
