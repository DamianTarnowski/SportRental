#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Starts SportRental.Api (lightweight backend without Key Vault)
#>

Write-Host ""
Write-Host "╔═══════════════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║                                                                   ║" -ForegroundColor Cyan
Write-Host "║          🚀 SportRental.Api - Lightweight Backend 🚀              ║" -ForegroundColor Cyan
Write-Host "║                                                                   ║" -ForegroundColor Cyan
Write-Host "╚═══════════════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

$projectRoot = $PSScriptRoot

# Terminal 1: Public API
Write-Host "🔧 Opening Public API terminal..." -ForegroundColor Cyan
Start-Process pwsh -ArgumentList "-NoExit", "-Command", "cd '$projectRoot\SportRental.Api'; Write-Host '🔌 Starting Public API...' -ForegroundColor Green; dotnet run"

# Wait for API to start
Write-Host "⏳ Waiting 3 seconds for API to start..." -ForegroundColor Yellow
Start-Sleep -Seconds 3

# Terminal 2: WASM Client
Write-Host "🎨 Opening WASM Client terminal..." -ForegroundColor Cyan
Start-Process pwsh -ArgumentList "-NoExit", "-Command", "cd '$projectRoot\SportRental.Client'; Write-Host '📱 Starting WASM Client...' -ForegroundColor Green; dotnet run"

# Wait for client to start
Start-Sleep -Seconds 5

# Open browser
Write-Host "🌐 Opening browser..." -ForegroundColor Green
Start-Process "http://localhost:5014"

Write-Host ""
Write-Host "════════════════════════════════════════════════════════════════" -ForegroundColor DarkGray
Write-Host ""
Write-Host "✅ Both services started in separate windows!" -ForegroundColor Green
Write-Host ""
Write-Host "📱 WASM Client:  http://localhost:5014" -ForegroundColor White
Write-Host "🔌 Public API:   http://localhost:5002" -ForegroundColor White
Write-Host ""
Write-Host "💡 Close each terminal window to stop the service" -ForegroundColor Yellow
Write-Host ""
Write-Host "⚠️  Note: SportRental.Api nie ma TestDataSeeder!" -ForegroundColor Yellow
Write-Host "   Produkty muszą być już w bazie (wcześniej zaseedowane przez Admin)" -ForegroundColor Yellow
Write-Host ""





















