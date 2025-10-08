#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Resets the database and seeds it with test data
.DESCRIPTION
    This script drops the database, recreates it with migrations, 
    and seeds test data from test-data.json
.EXAMPLE
    .\reset-and-seed-database.ps1
#>

Write-Host "╔══════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║                                                      ║" -ForegroundColor Cyan
Write-Host "║    🔄 RESET & SEED DATABASE WITH TEST DATA 🔄       ║" -ForegroundColor Cyan
Write-Host "║                                                      ║" -ForegroundColor Cyan
Write-Host "╚══════════════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

# Step 1: Drop database
Write-Host "🗑️  Step 1: Dropping database..." -ForegroundColor Yellow
dotnet ef database drop --force --project SportRental.Infrastructure --startup-project SportRental.Admin
if ($LASTEXITCODE -ne 0) {
    Write-Host "⚠️  Warning: Failed to drop database (might not exist)" -ForegroundColor Yellow
}
Write-Host ""

# Step 2: Apply migrations
Write-Host "📦 Step 2: Applying migrations..." -ForegroundColor Yellow
dotnet ef database update --project SportRental.Infrastructure --startup-project SportRental.Admin
if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Error: Failed to apply migrations" -ForegroundColor Red
    exit 1
}
Write-Host ""

# Step 3: Run application (seeding happens automatically on startup)
Write-Host "🌱 Step 3: Starting application to seed test data..." -ForegroundColor Yellow
Write-Host ""
Write-Host "ℹ️  The application will seed test data from test-data.json automatically" -ForegroundColor Cyan
Write-Host "ℹ️  Press Ctrl+C after you see 'Test data seeding completed successfully!'" -ForegroundColor Cyan
Write-Host ""
Write-Host "Starting in 3 seconds..." -ForegroundColor Gray
Start-Sleep -Seconds 3

# Run the app (user will need to Ctrl+C after seeding completes)
dotnet run --project SportRental.Admin --no-build

Write-Host ""
Write-Host "╔══════════════════════════════════════════════════════╗" -ForegroundColor Green
Write-Host "║                                                      ║" -ForegroundColor Green
Write-Host "║    ✅ DATABASE RESET & SEED COMPLETE! ✅            ║" -ForegroundColor Green
Write-Host "║                                                      ║" -ForegroundColor Green
Write-Host "╚══════════════════════════════════════════════════════╝" -ForegroundColor Green
Write-Host ""
Write-Host "📊 Seeded data:" -ForegroundColor Cyan
Write-Host "   • 3 Tenants (wypożyczalnie)" -ForegroundColor White
Write-Host "   • 3 CompanyInfos (z NIP, REGON)" -ForegroundColor White
Write-Host "   • ~16 Products" -ForegroundColor White
Write-Host "   • 5 Customers" -ForegroundColor White
Write-Host ""
Write-Host "🚀 Ready for E2E testing!" -ForegroundColor Green
Write-Host ""
