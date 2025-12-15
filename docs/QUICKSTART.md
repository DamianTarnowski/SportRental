# 🚀 Quick Start Guide

Get SportRental up and running in **5 minutes**!

> **Ostatnia aktualizacja:** Grudzień 2025

## ⚠️ Aktualna architektura

- **SportRental.Admin** - Panel + API (jedyny backend do uruchomienia)
- **SportRental.Client** - Blazor WASM
- **SportRental.Api** - ⏸️ WYŁĄCZONY
- **SportRental.MediaStorage** - ⏸️ WYŁĄCZONY (pliki w Azure Blob)

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [PostgreSQL 14+](https://www.postgresql.org/download/)
- [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli) (for Key Vault)
- [Node.js 18+](https://nodejs.org/) (for Blazor WASM)

## Step 1: Clone the Repository

```bash
git clone https://github.com/DamianTarnowski/SportRental.git
cd SportRental
```

## Step 2: Setup Configuration

### Option A: Azure Key Vault (Recommended)

1. **Create Azure Key Vault**:
   ```bash
   az login
   az keyvault create --name YOUR_VAULT_NAME --resource-group YOUR_RG --location eastus
   ```

2. **Add secrets** (see [SECURITY.md](../SECURITY.md) for full list):
   ```bash
   az keyvault secret set --vault-name YOUR_VAULT_NAME \
     --name "ConnectionStrings--DefaultConnection" \
     --value "Host=localhost;Database=sportrental;Username=postgres;Password=YOUR_PASSWORD"
   ```

3. **Update appsettings.json**:
   ```json
   {
     "KeyVault": {
       "Url": "https://YOUR_VAULT_NAME.vault.azure.net/"
     }
   }
   ```

### Option B: Local Development (Quick Test)

1. **Copy template**:
   ```bash
   cp appsettings.Development.json.template SportRental.Api/appsettings.Development.json
   cp appsettings.Development.json.template SportRental.Admin/appsettings.Development.json
   ```

2. **Edit files** and fill in your credentials

⚠️ **Never commit these files!** They are ignored by .gitignore.

## Step 3: Setup Database

```bash
cd SportRental.Admin
dotnet ef database update
cd ..
```

## Step 4: Run the Applications

### Option A: Visual Studio (Recommended)
1. Open `SportRentalHybrid.sln`
2. Click dropdown next to Start button
3. Select **"Admin + Client"** profile
4. Press F5 - both projects will start

### Option B: Terminal
```bash
# Terminal 1: Admin Panel + API
cd SportRental.Admin
dotnet run --urls "http://localhost:5001"

# Terminal 2: Client WASM
cd SportRental.Client
dotnet run --urls "http://localhost:5014"
```

> **Note:** `SportRental.Api` and `SportRental.MediaStorage` are **DISABLED**.
> API is hosted in Admin, files are stored in Azure Blob Storage.

## Step 5: Test the System

1. **Access Admin Panel**: https://localhost:7142
2. **Register** a new company
3. **Add products** to your catalog
4. **Create a rental** to test the flow

## 🎯 Next Steps

- Read [DEVELOPER_GUIDE.md](../doc/DEVELOPER_GUIDE.md) for detailed setup
- Check [API_DOCUMENTATION.md](../doc/API_DOCUMENTATION.md) for API reference
- Review [TESTING_GUIDE.md](../doc/TESTING_GUIDE.md) for running tests
- See [CONTRIBUTING.md](../CONTRIBUTING.md) to start contributing

## 🆘 Troubleshooting

### Database Connection Failed
- Ensure PostgreSQL is running
- Check connection string in configuration
- Verify database exists: `createdb sportrental`

### Azure Key Vault Access Denied
- Run `az login` to authenticate
- Check RBAC permissions on Key Vault
- Verify Key Vault URL is correct

### Port Already in Use
- Change ports in `Properties/launchSettings.json`
- Or stop conflicting applications

### Migration Failed
- Delete database: `dropdb sportrental && createdb sportrental`
- Run migrations again: `dotnet ef database update`

## 📚 Additional Resources

- [Architecture Overview](../doc/ARCHITECTURE.md)
- [Security Best Practices](../SECURITY.md)
- [Deployment Guide](../doc/setup/DEPLOYMENT.md)

---

**Need help?** Open an issue on GitHub or contact: hdtdtr@gmail.com
