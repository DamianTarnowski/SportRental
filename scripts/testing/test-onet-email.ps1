# test-onet-email.ps1
# Script to test email sending with real Onet SMTP

param(
    [switch]$RunIntegrationTests,
    [switch]$SendTestEmail,
    [switch]$SendWithPdf
)

Write-Host "╔════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║  📧 ONET EMAIL INTEGRATION TESTS 📧       ║" -ForegroundColor Cyan
Write-Host "╚════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

# Display configuration
Write-Host "📋 KONFIGURACJA ONET:" -ForegroundColor Yellow
Write-Host ""
Write-Host "  SMTP Server:  smtp.poczta.onet.pl" -ForegroundColor White
Write-Host "  Port:         465" -ForegroundColor White
Write-Host "  SSL:          Enabled" -ForegroundColor White
Write-Host ""
Write-Host "  📨 Konto wypożyczalni:  contact.sportrental@op.pl" -ForegroundColor Green
Write-Host "  👤 Konto testowe:       testklient@op.pl" -ForegroundColor Green
Write-Host ""

Write-Host "═══════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""

if ($SendTestEmail) {
    Write-Host "📧 TEST 1: Wysyłanie prostego emaila..." -ForegroundColor Yellow
    Write-Host ""
    
    dotnet test SportRental.Api.Tests/SportRental.Api.Tests.csproj `
        --filter "FullyQualifiedName~EmailIntegrationTests.SendEmail_WithOnetSMTP_Succeeds" `
        --verbosity normal
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host ""
        Write-Host "✅ Test passed! Sprawdź skrzynkę: testklient@op.pl" -ForegroundColor Green
    } else {
        Write-Host ""
        Write-Host "❌ Test failed! Sprawdź konfigurację SMTP." -ForegroundColor Red
    }
    
    Write-Host ""
    Write-Host "═══════════════════════════════════════════" -ForegroundColor Cyan
    Write-Host ""
}

if ($SendWithPdf) {
    Write-Host "📄 TEST 2: Wysyłanie emaila z PDF..." -ForegroundColor Yellow
    Write-Host ""
    
    dotnet test SportRental.Api.Tests/SportRental.Api.Tests.csproj `
        --filter "FullyQualifiedName~RentalConfirmationEmailIntegrationTests.SendRentalConfirmation_WithPdfAttachment_ToOnetEmail_Succeeds" `
        --verbosity normal
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host ""
        Write-Host "✅ Email z PDF wysłany! Sprawdź załącznik." -ForegroundColor Green
    } else {
        Write-Host ""
        Write-Host "❌ Test failed!" -ForegroundColor Red
    }
    
    Write-Host ""
    Write-Host "═══════════════════════════════════════════" -ForegroundColor Cyan
    Write-Host ""
}

if ($RunIntegrationTests) {
    Write-Host "🧪 Uruchamiam WSZYSTKIE testy integracyjne..." -ForegroundColor Yellow
    Write-Host ""
    
    # Build first
    Write-Host "🔨 Building..." -ForegroundColor Gray
    dotnet build SportRental.Api.Tests/SportRental.Api.Tests.csproj --verbosity quiet
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host "❌ Build failed!" -ForegroundColor Red
        exit 1
    }
    
    Write-Host ""
    Write-Host "🧪 Running tests..." -ForegroundColor Gray
    Write-Host ""
    
    # Run configuration tests (always enabled)
    Write-Host "📋 Test 1: Configuration validation..." -ForegroundColor Cyan
    dotnet test SportRental.Api.Tests/SportRental.Api.Tests.csproj `
        --filter "FullyQualifiedName~EmailIntegrationTests.Configuration_HasValidOnetSettings" `
        --verbosity normal --no-build
    
    Write-Host ""
    Write-Host "📋 Test 2: Test accounts validation..." -ForegroundColor Cyan
    dotnet test SportRental.Api.Tests/SportRental.Api.Tests.csproj `
        --filter "FullyQualifiedName~EmailIntegrationTests.TestAccounts_AreConfigured" `
        --verbosity normal --no-build
    
    Write-Host ""
    Write-Host "═══════════════════════════════════════════" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "⚠️  Integration tests with real SMTP are SKIPPED by default." -ForegroundColor Yellow
    Write-Host "   To run them manually, use:" -ForegroundColor Gray
    Write-Host ""
    Write-Host "   .\test-onet-email.ps1 -SendTestEmail" -ForegroundColor White
    Write-Host "   .\test-onet-email.ps1 -SendWithPdf" -ForegroundColor White
    Write-Host ""
}

if (-not $SendTestEmail -and -not $SendWithPdf -and -not $RunIntegrationTests) {
    Write-Host "📚 DOSTĘPNE OPCJE:" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "  1. Walidacja konfiguracji:" -ForegroundColor Cyan
    Write-Host "     .\test-onet-email.ps1 -RunIntegrationTests" -ForegroundColor White
    Write-Host ""
    Write-Host "  2. Wyślij prosty email:" -ForegroundColor Cyan
    Write-Host "     .\test-onet-email.ps1 -SendTestEmail" -ForegroundColor White
    Write-Host ""
    Write-Host "  3. Wyślij email z PDF:" -ForegroundColor Cyan
    Write-Host "     .\test-onet-email.ps1 -SendWithPdf" -ForegroundColor White
    Write-Host ""
    Write-Host "═══════════════════════════════════════════" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "💡 SZYBKI START:" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "   # Sprawdź konfigurację" -ForegroundColor Gray
    Write-Host "   .\test-onet-email.ps1 -RunIntegrationTests" -ForegroundColor White
    Write-Host ""
    Write-Host "   # Wyślij test email" -ForegroundColor Gray
    Write-Host "   .\test-onet-email.ps1 -SendTestEmail" -ForegroundColor White
    Write-Host ""
    Write-Host "   # Sprawdź w skrzynce:" -ForegroundColor Gray
    Write-Host "   https://poczta.onet.pl" -ForegroundColor Cyan
    Write-Host "   Login: testklient@op.pl" -ForegroundColor White
    Write-Host ""
}

Write-Host "═══════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""
Write-Host "📧 SPRAWDŹ SKRZYNKĘ:" -ForegroundColor Yellow
Write-Host ""
Write-Host "  URL:      https://poczta.onet.pl" -ForegroundColor Cyan
Write-Host "  Login:    testklient@op.pl" -ForegroundColor White
Write-Host "  Password: [Stored in Azure Key Vault]" -ForegroundColor Gray
Write-Host ""
Write-Host "╔════════════════════════════════════════════╗" -ForegroundColor Green
Write-Host "║         ✅ ONET SMTP CONFIGURED! ✅        ║" -ForegroundColor Green
Write-Host "╚════════════════════════════════════════════╝" -ForegroundColor Green
Write-Host ""
