#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Starts SportRental in development mode
.DESCRIPTION
    This script starts both the Admin API (backend) and WASM Client (frontend)
    for full-stack development. Press Ctrl+C to stop all services.
.PARAMETER ApiOnly
    Start only the Admin API without the WASM Client
.PARAMETER ClientOnly
    Start only the WASM Client (requires API to be running separately)
.PARAMETER UsePublicApi
    Use SportRental.Api instead of SportRental.Admin
.EXAMPLE
    .\start-dev.ps1
    Starts both Admin API and WASM Client
.EXAMPLE
    .\start-dev.ps1 -ApiOnly
    Starts only the Admin API
.EXAMPLE
    .\start-dev.ps1 -UsePublicApi
    Starts Public API and WASM Client
#>

param(
    [switch]$ApiOnly,
    [switch]$ClientOnly,
    [switch]$UsePublicApi
)

$ErrorActionPreference = "Stop"

# Banner
Write-Host ""
Write-Host "╔═══════════════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║                                                                   ║" -ForegroundColor Cyan
Write-Host "║              🚀 SportRental - Development Mode 🚀                 ║" -ForegroundColor Cyan
Write-Host "║                                                                   ║" -ForegroundColor Cyan
Write-Host "╚═══════════════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

# Determine what to start
$startApi = !$ClientOnly
$startClient = !$ApiOnly
$apiProject = if ($UsePublicApi) { "SportRental.Api" } else { "SportRental.Admin" }

if ($startApi -and $startClient) {
    Write-Host "📋 Starting: $apiProject (Backend) + WASM Client (Frontend)" -ForegroundColor Yellow
} elseif ($startApi) {
    Write-Host "📋 Starting: $apiProject (Backend only)" -ForegroundColor Yellow
} elseif ($startClient) {
    Write-Host "📋 Starting: WASM Client (Frontend only)" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "════════════════════════════════════════════════════════════════" -ForegroundColor DarkGray
Write-Host ""

# Job tracking
$jobs = @()

try {
    # Start API
    if ($startApi) {
        Write-Host "🔧 Starting $apiProject..." -ForegroundColor Cyan
        
        $apiJob = Start-Job -ScriptBlock {
            param($SolutionPath, $ProjectName)
            $projectFolder = Join-Path $SolutionPath $ProjectName
            Set-Location $projectFolder
            dotnet run
        } -ArgumentList (Get-Location).Path, $apiProject
        
        $jobs += $apiJob
        
        Write-Host "   ✅ $apiProject started (Job ID: $($apiJob.Id))" -ForegroundColor Green
        
        # Wait for API to be ready
        Write-Host "   ⏳ Waiting for API to start..." -ForegroundColor Yellow
        Start-Sleep -Seconds 5
        
        Write-Host ""
    }

    # Start Client
    if ($startClient) {
        Write-Host "🎨 Starting WASM Client..." -ForegroundColor Cyan
        
        $clientJob = Start-Job -ScriptBlock {
            param($ProjectPath)
            Set-Location "$ProjectPath\SportRental.Client"
            dotnet run
        } -ArgumentList (Get-Location).Path
        
        $jobs += $clientJob
        
        Write-Host "   ✅ WASM Client started (Job ID: $($clientJob.Id))" -ForegroundColor Green
        Write-Host ""
    }

    # Show URLs
    Write-Host "════════════════════════════════════════════════════════════════" -ForegroundColor DarkGray
    Write-Host ""
    Write-Host "🌐 URLS:" -ForegroundColor Green
    Write-Host ""
    
    if ($startClient) {
        Write-Host "   📱 WASM Client:  http://localhost:5014" -ForegroundColor White
        Write-Host "                    https://localhost:7083" -ForegroundColor White
    }
    
    if ($startApi) {
        if ($UsePublicApi) {
            Write-Host "   🔌 Public API:   http://localhost:5002" -ForegroundColor White
            Write-Host "                    https://localhost:7002" -ForegroundColor White
        } else {
            Write-Host "   🔌 Admin API:    http://localhost:5001" -ForegroundColor White
            Write-Host "                    https://localhost:7001" -ForegroundColor White
            Write-Host "   🎛️  Admin Panel:  http://localhost:5001" -ForegroundColor White
        }
    }
    
    Write-Host ""
    Write-Host "════════════════════════════════════════════════════════════════" -ForegroundColor DarkGray
    Write-Host ""
    Write-Host "💡 TIPS:" -ForegroundColor Yellow
    Write-Host "   • Press Ctrl+C to stop all services" -ForegroundColor Gray
    Write-Host "   • Check job status: Get-Job" -ForegroundColor Gray
    Write-Host "   • View logs: Receive-Job -Id <ID> -Keep" -ForegroundColor Gray
    Write-Host ""
    Write-Host "════════════════════════════════════════════════════════════════" -ForegroundColor DarkGray
    Write-Host ""
    Write-Host "✨ All services started! Opening browser..." -ForegroundColor Green
    Write-Host ""
    
    # Open browser
    if ($startClient) {
        Start-Sleep -Seconds 2
        Start-Process "http://localhost:5014"
    } elseif ($startApi -and !$UsePublicApi) {
        Start-Sleep -Seconds 2
        Start-Process "http://localhost:5001"
    }
    
    # Stream logs from all jobs
    Write-Host "📊 Streaming logs (press Ctrl+C to stop):" -ForegroundColor Cyan
    Write-Host ""
    
    while ($true) {
        foreach ($job in $jobs) {
            $output = Receive-Job -Job $job -ErrorAction SilentlyContinue
            if ($output) {
                Write-Host $output
            }
            
            if ($job.State -eq "Failed") {
                Write-Host "❌ Job $($job.Id) failed!" -ForegroundColor Red
                throw "Job failed"
            }
        }
        Start-Sleep -Milliseconds 500
    }
}
catch {
    Write-Host ""
    Write-Host "🛑 Stopping all services..." -ForegroundColor Yellow
}
finally {
    # Cleanup
    foreach ($job in $jobs) {
        if ($job.State -eq "Running") {
            Stop-Job -Job $job -ErrorAction SilentlyContinue
        }
        Remove-Job -Job $job -Force -ErrorAction SilentlyContinue
    }
    
    Write-Host ""
    Write-Host "✅ All services stopped." -ForegroundColor Green
    Write-Host ""
}





















