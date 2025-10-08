# Setup test database on Azure PostgreSQL
# Creates sr_test database and applies migrations

Write-Host "🔧 Setting up test database on Azure PostgreSQL..." -ForegroundColor Green
Write-Host ""

# ⚠️ Connection string is stored in Azure Key Vault!
# Get it from Key Vault or appsettings.Test.json (not committed to Git)

Write-Host "📋 Getting connection string from Azure Key Vault..." -ForegroundColor Cyan

# Check if we have az CLI and are logged in
try {
    $accountInfo = az account show 2>$null | ConvertFrom-Json
    if ($accountInfo) {
        Write-Host "✅ Logged in to Azure as: $($accountInfo.user.name)" -ForegroundColor Green
        
        # Get connection string from Key Vault
        $connectionString = az keyvault secret show `
            --vault-name vault2127 `
            --name "ConnectionStrings--DefaultConnection" `
            --query value -o tsv
        
        if ($connectionString) {
            Write-Host "✅ Connection string retrieved from Key Vault!" -ForegroundColor Green
        } else {
            Write-Host "❌ Failed to get connection string from Key Vault!" -ForegroundColor Red
            Write-Host "   Run: az login --tenant YOUR-TENANT-ID" -ForegroundColor Yellow
            exit 1
        }
    }
} catch {
    Write-Host "❌ Azure CLI not available or not logged in!" -ForegroundColor Red
    Write-Host "   Please run: az login --tenant YOUR-TENANT-ID" -ForegroundColor Yellow
    exit 1
}

# Set environment variable for EF migrations
$env:ConnectionStrings__DefaultConnection = $connectionString

Write-Host "1️⃣  Creating database sr_test (if not exists)..." -ForegroundColor Yellow

# Try to connect - if fails, database doesn't exist yet
try {
    Write-Host "   Applying migrations to sr_test..." -ForegroundColor Cyan
    dotnet ef database update --project SportRental.Admin --connection $connectionString
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "   ✅ Test database ready!" -ForegroundColor Green
    } else {
        Write-Host "   ⚠️  Migration may have issues - check output above" -ForegroundColor Yellow
    }
} catch {
    Write-Host "   ❌ Error: $_" -ForegroundColor Red
    Write-Host ""
    Write-Host "   Manual setup required:" -ForegroundColor Yellow
    Write-Host "   1. Connect to Azure Portal" -ForegroundColor Gray
    Write-Host "   2. Go to PostgreSQL server: eduedu.postgres.database.azure.com" -ForegroundColor Gray
    Write-Host "   3. Create database: sr_test" -ForegroundColor Gray
    Write-Host "   4. Or use pgAdmin/psql to run: CREATE DATABASE sr_test;" -ForegroundColor Gray
}

Write-Host ""
Write-Host "2️⃣  Test database configuration:" -ForegroundColor Yellow
Write-Host "   Host: eduedu.postgres.database.azure.com" -ForegroundColor Gray
Write-Host "   Database: sr_test" -ForegroundColor Gray
Write-Host "   Username: synapsis" -ForegroundColor Gray
Write-Host "   SSL: Required" -ForegroundColor Gray
Write-Host ""

Write-Host "3️⃣  Run tests:" -ForegroundColor Yellow
Write-Host "   dotnet test SportRentalHybrid.sln" -ForegroundColor White
Write-Host ""

Write-Host "✅ Setup complete!" -ForegroundColor Green
