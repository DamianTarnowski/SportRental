#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Simple startup script for SportRental (no background jobs)
.DESCRIPTION
    Opens two terminal windows - one for Admin API, one for WASM Client
#>

Write-Host ""
Write-Host "╔═══════════════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║                                                                   ║" -ForegroundColor Cyan
Write-Host "║              🚀 SportRental - Development Mode 🚀                 ║" -ForegroundColor Cyan
Write-Host "║                                                                   ║" -ForegroundColor Cyan
Write-Host "╚═══════════════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

$projectRoot = $PSScriptRoot

# Terminal 1: Admin API
Write-Host "🔧 Opening Admin API terminal..." -ForegroundColor Cyan
Start-Process pwsh -ArgumentList "-NoExit", "-Command", "cd '$projectRoot\SportRental.Admin'; Write-Host '🔌 Starting Admin API...' -ForegroundColor Green; dotnet run"

# Wait a bit for API to start
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
Write-Host "🔌 Admin API:    http://localhost:5001" -ForegroundColor White
Write-Host ""
Write-Host "💡 Close each terminal window to stop the service" -ForegroundColor Yellow
Write-Host ""






















