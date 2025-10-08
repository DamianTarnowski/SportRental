# Test płatności Stripe dla SportRental
# Symuluje udaną płatność w sandboxie

Write-Host "💳 Stripe Payment Test - SportRental" -ForegroundColor Green
Write-Host ""

if (!(Test-Path "stripe.exe")) {
    Write-Host "❌ Nie znaleziono stripe.exe!" -ForegroundColor Red
    exit 1
}

Write-Host "🎯 Test płatności w sandboxie:" -ForegroundColor Cyan
Write-Host ""
Write-Host "1️⃣  Karty testowe:" -ForegroundColor Yellow
Write-Host "  ✅ Sukces:          4242 4242 4242 4242" -ForegroundColor Green
Write-Host "  ❌ Odrzucona:       4000 0000 0000 0002" -ForegroundColor Red
Write-Host "  🔐 3D Secure:       4000 0025 0000 3155" -ForegroundColor Magenta
Write-Host "  💰 Brak środków:    4000 0000 0000 9995" -ForegroundColor Yellow
Write-Host ""
Write-Host "2️⃣  BLIK test code: 777777" -ForegroundColor Yellow
Write-Host ""
Write-Host "3️⃣  CVV: dowolne 3 cyfry | Data: dowolna przyszła" -ForegroundColor Gray
Write-Host ""

Write-Host "📋 Dostępne komendy Stripe CLI:" -ForegroundColor Cyan
Write-Host ""
Write-Host "  # Symuluj udany webhook payment_intent.succeeded:" -ForegroundColor Gray
Write-Host "  .\stripe.exe trigger payment_intent.succeeded" -ForegroundColor White
Write-Host ""
Write-Host "  # Symuluj nieudaną płatność:" -ForegroundColor Gray
Write-Host "  .\stripe.exe trigger payment_intent.payment_failed" -ForegroundColor White
Write-Host ""
Write-Host "  # Lista wszystkich eventów:" -ForegroundColor Gray
Write-Host "  .\stripe.exe events list" -ForegroundColor White
Write-Host ""
Write-Host "  # Lista Payment Intents:" -ForegroundColor Gray
Write-Host "  .\stripe.exe payment_intents list" -ForegroundColor White
Write-Host ""
Write-Host "  # Szczegóły Payment Intent:" -ForegroundColor Gray
Write-Host "  .\stripe.exe payment_intents retrieve pi_xxx" -ForegroundColor White
Write-Host ""
Write-Host "  # Lista Checkout Sessions:" -ForegroundColor Gray
Write-Host "  .\stripe.exe checkout sessions list" -ForegroundColor White
Write-Host ""

Write-Host "🚀 Gotowy do testowania!" -ForegroundColor Green
Write-Host ""
Write-Host "Instrukcje:" -ForegroundColor Yellow
Write-Host "1. Uruchom API:     cd SportRental.Api && dotnet run" -ForegroundColor White
Write-Host "2. Uruchom Client:  cd SportRental.Client && dotnet run" -ForegroundColor White
Write-Host "3. Uruchom webhooks: .\start-stripe-webhooks.ps1" -ForegroundColor White
Write-Host "4. Testuj płatności na http://localhost:5014" -ForegroundColor White
Write-Host ""
