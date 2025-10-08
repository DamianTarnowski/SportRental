# Stripe Webhook Forwarder dla SportRental
# Uruchamia lokalne przekierowanie webhooków Stripe do API

Write-Host "🚀 Uruchamianie Stripe webhook forwarding..." -ForegroundColor Green
Write-Host ""

# Sprawdź czy stripe.exe istnieje
if (!(Test-Path "stripe.exe")) {
    Write-Host "❌ Nie znaleziono stripe.exe w tym katalogu!" -ForegroundColor Red
    Write-Host "Pobierz z: https://github.com/stripe/stripe-cli/releases" -ForegroundColor Yellow
    exit 1
}

Write-Host "📋 Stripe test keys są przechowywane w Azure Key Vault" -ForegroundColor Cyan
Write-Host "  (Aplikacja pobiera je automatycznie)" -ForegroundColor Gray
Write-Host ""

Write-Host "🔐 Logowanie do Stripe..." -ForegroundColor Yellow
Write-Host "  (Jeśli pierwszy raz, wklej klucz API z Stripe Dashboard)" -ForegroundColor Gray
Write-Host ""

# Login (interaktywne, jeśli nie zalogowany)
.\stripe.exe login

Write-Host ""
Write-Host "🌐 Przekierowywanie webhooków z Stripe do lokalnego API..." -ForegroundColor Yellow
Write-Host "  API URL: https://localhost:7142/api/webhooks/stripe" -ForegroundColor Gray
Write-Host ""
Write-Host "⚠️  WAŻNE: Skopiuj 'webhook signing secret' (whsec_...) i wklej do appsettings.Development.json!" -ForegroundColor Red
Write-Host ""

# Forward webhooks
.\stripe.exe listen --forward-to https://localhost:7142/api/webhooks/stripe

Write-Host ""
Write-Host "✅ Stripe webhook forwarding zakończony." -ForegroundColor Green
