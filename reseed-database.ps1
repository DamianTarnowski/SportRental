#!/usr/bin/env pwsh
# Skrypt do reseedowania bazy danych

Write-Host "`n🌱 RESEED BAZY DANYCH" -ForegroundColor Green
Write-Host "══════════════════════════════════════`n" -ForegroundColor Green

# 1. Zatrzymaj backend
Write-Host "1️⃣  Zatrzymuję backend..." -ForegroundColor Cyan
Get-Process -Name "SportRental" -ErrorAction SilentlyContinue | Stop-Process -Force 2>$null
Get-Process -Name "dotnet" -ErrorAction SilentlyContinue | Where-Object { 
    $_.MainWindowTitle -like "*Sport*" 
} | Stop-Process -Force 2>$null
Start-Sleep -Seconds 2
Write-Host "   ✅ Backend zatrzymany`n" -ForegroundColor Green

# 2. Kasuj bazę
Write-Host "2️⃣  Kasowanie bazy danych..." -ForegroundColor Cyan
Set-Location -Path "$PSScriptRoot\SportRental.Admin"
dotnet ef database drop --force 2>&1 | Out-Null
if ($LASTEXITCODE -eq 0) {
    Write-Host "   ✅ Baza skasowana`n" -ForegroundColor Green
} else {
    Write-Host "   ⚠️  Baza nie istniała lub błąd`n" -ForegroundColor Yellow
}

# 3. Uruchom backend (automatycznie utworzy bazę i zaseeduje)
Write-Host "3️⃣  Uruchamiam backend..." -ForegroundColor Cyan
Write-Host "   (automatycznie utworzy bazę i załaduje dane z test-data.json)" -ForegroundColor DarkGray
Start-Process pwsh -ArgumentList "-NoExit", "-Command", @"
cd '$PSScriptRoot\SportRental.Admin'
Write-Host '🚀 Backend + Seeder' -ForegroundColor Green
dotnet run
"@ -WindowStyle Minimized

Write-Host "   ⏳ Czekam 30s na seeding..." -ForegroundColor Yellow
Start-Sleep -Seconds 30

# 4. Sprawdź wyniki
Write-Host "`n4️⃣  Sprawdzam załadowane dane...`n" -ForegroundColor Cyan
Set-Location -Path $PSScriptRoot

try {
    $response = Invoke-WebRequest -Uri "http://localhost:5001/api/products?page=1&pageSize=500" `
        -Method Get -TimeoutSec 5 -ErrorAction Stop
    $products = $response.Content | ConvertFrom-Json
    
    Write-Host "   ✅ Backend odpowiada!" -ForegroundColor Green
    Write-Host "   📦 Produktów w bazie: $($products.Count)" -ForegroundColor Cyan
    Write-Host "   🗂️  Kategorii: $(($products | Group-Object Category).Count)" -ForegroundColor Cyan
    Write-Host "`n══════════════════════════════════════" -ForegroundColor Green
    Write-Host "  ✅ RESEED ZAKOŃCZONY SUKCESEM!" -ForegroundColor Green
    Write-Host "══════════════════════════════════════`n" -ForegroundColor Green
} catch {
    Write-Host "   ⚠️  Backend jeszcze się uruchamia..." -ForegroundColor Yellow
    Write-Host "   Sprawdź po chwili: http://localhost:5001/api/products`n" -ForegroundColor DarkGray
}








