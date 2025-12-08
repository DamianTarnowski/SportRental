# ☁️ Azure Deployment Guide - SportRental

Kompletny przewodnik publikacji SportRental na Microsoft Azure.

---

## 📋 **Spis treści**

1. [Przegląd Architektury](#przegląd-architektury)
2. [Wymagania](#wymagania)
3. [Przygotowanie Azure](#przygotowanie-azure)
4. [Deployment Krok po Kroku](#deployment-krok-po-kroku)
5. [Konfiguracja DNS i SSL](#konfiguracja-dns-i-ssl)
6. [Monitorowanie](#monitorowanie)
7. [Troubleshooting](#troubleshooting)

---

## 🏗️ **Przegląd Architektury**

SportRental składa się z 4 aplikacji + 2 baz danych:

```
┌─────────────────────────────────────────────────────────┐
│                    AZURE RESOURCES                      │
├─────────────────────────────────────────────────────────┤
│                                                         │
│  ┌──────────────────┐  ┌──────────────────┐           │
│  │ App Service      │  │ App Service      │           │
│  │ (Admin Panel)    │  │ (Public API)     │           │
│  │ Blazor Server    │  │ Minimal APIs     │           │
│  └──────────────────┘  └──────────────────┘           │
│           │                     │                      │
│           └──────────┬──────────┘                      │
│                      │                                 │
│         ┌────────────▼────────────┐                   │
│         │ Azure Database          │                   │
│         │ for PostgreSQL          │                   │
│         │ (Flexible Server)       │                   │
│         └─────────────────────────┘                   │
│                                                        │
│  ┌──────────────────┐  ┌──────────────────┐          │
│  │ Static Web App   │  │ App Service      │          │
│  │ (WASM Client)    │  │ (Media Storage)  │          │
│  │ Blazor WASM      │  │ Microservice     │          │
│  └──────────────────┘  └──────────────────┘          │
│           │                     │                     │
│           │            ┌────────▼────────┐           │
│           │            │ Blob Storage    │           │
│           │            │ (Media files)   │           │
│           │            └─────────────────┘           │
│           │                                           │
│  ┌────────▼────────────────────────────┐            │
│  │        Azure Key Vault              │            │
│  │        (Secrets)                    │            │
│  └─────────────────────────────────────┘            │
│                                                      │
└──────────────────────────────────────────────────────┘
```

---

## 📝 **Wymagania**

### **Lokalne narzędzia:**
- ✅ [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli) (już masz)
- ✅ [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- ✅ [Visual Studio Code](https://code.visualstudio.com/) lub Visual Studio 2022

### **Azure Subscription:**
- 💰 Azure subscription (już masz)
- 💳 Karta kredytowa (dla weryfikacji, free tier dostępny)

### **Szacowane koszty miesięczne:**

| Usługa | Plan | Koszt/miesiąc |
|--------|------|---------------|
| **App Service** (Admin) | B1 Basic | ~€50 |
| **App Service** (API) | B1 Basic | ~€50 |
| **App Service** (Media) | B1 Basic | ~€50 |
| **Static Web App** (WASM) | Free | €0 |
| **PostgreSQL** | B_Gen5_1 | ~€30 |
| **Blob Storage** | Standard | ~€5-20 |
| **Key Vault** | Standard | ~€2 |
| **RAZEM** | | **~€187-207/miesiąc** |

**💡 TIP:** Możesz zacząć od Free/Shared tier i skalować w górę!

---

## 🚀 **Przygotowanie Azure**

### **1. Zaloguj się do Azure**

```bash
az login --tenant <YOUR_TENANT_ID>
```

### **2. Ustaw domyślną subskrypcję**

```bash
az account set --subscription "<YOUR_SUBSCRIPTION_ID>"
```

### **3. Sprawdź czy wszystko działa**

```bash
az account show
```

---

## 📦 **Deployment Krok po Kroku**

### **KROK 1: Utwórz Resource Group**

```bash
# Utwórz grupę zasobów w Europie Zachodniej (najbliżej Polski)
az group create \
  --name SportRental-Production \
  --location westeurope
```

---

### **KROK 2: Utwórz PostgreSQL Database**

```bash
# Utwórz PostgreSQL Flexible Server
az postgres flexible-server create \
  --name sportrental-db-prod \
  --resource-group SportRental-Production \
  --location westeurope \
  --admin-user sportadmin \
  --admin-password "<YOUR_SECURE_PASSWORD>" \
  --sku-name Standard_B1ms \
  --tier Burstable \
  --storage-size 32 \
  --version 14 \
  --public-access 0.0.0.0-255.255.255.255

# Utwórz bazę danych
az postgres flexible-server db create \
  --resource-group SportRental-Production \
  --server-name sportrental-db-prod \
  --database-name sportrental
```

**⚠️ WAŻNE:** Zapisz hasło! Będzie potrzebne w Key Vault.

---

### **KROK 3: Dodaj Connection String do Key Vault**

```bash
# Connection string
CONNECTION_STRING="Host=<YOUR_DB_SERVER>.postgres.database.azure.com;Port=5432;Database=sportrental;Username=sportadmin;Password=<YOUR_SECURE_PASSWORD>;SSL Mode=Require;Trust Server Certificate=true"

# Dodaj do Key Vault
az keyvault secret set \
  --vault-name <YOUR_KEYVAULT_NAME> \
  --name "ConnectionStrings--DefaultConnection" \
  --value "$CONNECTION_STRING"
```

---

### **KROK 4: Utwórz App Service Plan**

```bash
# Plan dla wszystkich App Services
az appservice plan create \
  --name SportRental-Plan \
  --resource-group SportRental-Production \
  --location westeurope \
  --sku B1 \
  --is-linux
```

**💡 TIP:** B1 to najtańszy plan production-ready (~€50/miesiąc na app)

---

### **KROK 5: Utwórz App Services**

#### **5.1. Admin Panel**

```bash
az webapp create \
  --name sportrental-admin \
  --resource-group SportRental-Production \
  --plan SportRental-Plan \
  --runtime "DOTNET|9.0"

# Skonfiguruj Key Vault
az webapp identity assign \
  --name sportrental-admin \
  --resource-group SportRental-Production

# Pobierz Managed Identity ID
ADMIN_IDENTITY=$(az webapp identity show \
  --name sportrental-admin \
  --resource-group SportRental-Production \
  --query principalId -o tsv)

# Daj dostęp do Key Vault
az keyvault set-policy \
  --name <YOUR_KEYVAULT_NAME> \
  --object-id $ADMIN_IDENTITY \
  --secret-permissions get list
```

#### **5.2. Public API**

```bash
az webapp create \
  --name sportrental-api \
  --resource-group SportRental-Production \
  --plan SportRental-Plan \
  --runtime "DOTNET|9.0"

# Skonfiguruj Key Vault
az webapp identity assign \
  --name sportrental-api \
  --resource-group SportRental-Production

API_IDENTITY=$(az webapp identity show \
  --name sportrental-api \
  --resource-group SportRental-Production \
  --query principalId -o tsv)

az keyvault set-policy \
  --name <YOUR_KEYVAULT_NAME> \
  --object-id $API_IDENTITY \
  --secret-permissions get list
```

#### **5.3. Media Storage**

```bash
az webapp create \
  --name sportrental-media \
  --resource-group SportRental-Production \
  --plan SportRental-Plan \
  --runtime "DOTNET|9.0"

# Skonfiguruj Key Vault
az webapp identity assign \
  --name sportrental-media \
  --resource-group SportRental-Production

MEDIA_IDENTITY=$(az webapp identity show \
  --name sportrental-media \
  --resource-group SportRental-Production \
  --query principalId -o tsv)

az keyvault set-policy \
  --name <YOUR_KEYVAULT_NAME> \
  --object-id $MEDIA_IDENTITY \
  --secret-permissions get list
```

---

### **KROK 6: Skonfiguruj App Settings**

#### **6.1. Admin Panel Settings**

```bash
az webapp config appsettings set \
  --name sportrental-admin \
  --resource-group SportRental-Production \
  --settings \
    "KeyVault__Url=https://<YOUR_KEYVAULT_NAME>.vault.azure.net/" \
    "ASPNETCORE_ENVIRONMENT=Production" \
    "Storage__Provider=AzureBlob"
```

#### **6.2. API Settings**

```bash
az webapp config appsettings set \
  --name sportrental-api \
  --resource-group SportRental-Production \
  --settings \
    "KeyVault__Url=https://<YOUR_KEYVAULT_NAME>.vault.azure.net/" \
    "ASPNETCORE_ENVIRONMENT=Production"
```

#### **6.3. Media Storage Settings**

```bash
az webapp config appsettings set \
  --name sportrental-media \
  --resource-group SportRental-Production \
  --settings \
    "KeyVault__Url=https://<YOUR_KEYVAULT_NAME>.vault.azure.net/" \
    "ASPNETCORE_ENVIRONMENT=Production"
```

---

### **KROK 7: Deploy Aplikacji**

#### **7.1. Przygotuj buildy**

```bash
# Admin Panel
cd SportRental.Admin
dotnet publish -c Release -o ./publish
cd ..

# Public API
cd SportRental.Api
dotnet publish -c Release -o ./publish
cd ..

# Media Storage
cd SportRental.MediaStorage
dotnet publish -c Release -o ./publish
cd ..
```

#### **7.2. Deploy przez Azure CLI**

**Admin Panel:**
```bash
cd SportRental.Admin/publish
zip -r admin.zip *
az webapp deployment source config-zip \
  --name sportrental-admin \
  --resource-group SportRental-Production \
  --src admin.zip
cd ../..
```

**Public API:**
```bash
cd SportRental.Api/publish
zip -r api.zip *
az webapp deployment source config-zip \
  --name sportrental-api \
  --resource-group SportRental-Production \
  --src api.zip
cd ../..
```

**Media Storage:**
```bash
cd SportRental.MediaStorage/publish
zip -r media.zip *
az webapp deployment source config-zip \
  --name sportrental-media \
  --resource-group SportRental-Production \
  --src media.zip
cd ../..
```

---

### **KROK 8: Uruchom Migracje EF Core**

```bash
# Połącz się z bazą i uruchom migracje
cd SportRental.Admin

# Ustaw connection string tymczasowo
export ConnectionStrings__DefaultConnection="Host=<YOUR_DB_SERVER>.postgres.database.azure.com;Port=5432;Database=sportrental;Username=sportadmin;Password=<YOUR_SECURE_PASSWORD>;SSL Mode=Require"

# Uruchom migracje
dotnet ef database update

cd ..
```

---

### **KROK 9: Deploy Blazor WASM (Static Web App)**

```bash
# Zainstaluj Static Web Apps CLI
npm install -g @azure/static-web-apps-cli

# Utwórz Static Web App
az staticwebapp create \
  --name sportrental-client \
  --resource-group SportRental-Production \
  --location westeurope \
  --sku Free

# Build WASM
cd SportRental.Client
dotnet publish -c Release -o ./publish

# Deploy
az staticwebapp deploy \
  --name sportrental-client \
  --resource-group SportRental-Production \
  --app-location ./publish/wwwroot
```

---

### **KROK 10: Skonfiguruj CORS w API**

```bash
az webapp cors add \
  --name sportrental-api \
  --resource-group SportRental-Production \
  --allowed-origins \
    "https://sportrental-admin.azurewebsites.net" \
    "https://sportrental-client.azurestaticapps.net"
```

---

## 🌐 **URLs Twojej Aplikacji**

Po deployment będziesz mieć:

| Aplikacja | URL |
|-----------|-----|
| **Admin Panel** | https://sportrental-admin.azurewebsites.net |
| **Public API** | https://sportrental-api.azurewebsites.net |
| **WASM Client** | https://sportrental-client.azurestaticapps.net |
| **Media Storage** | https://sportrental-media.azurewebsites.net |

---

## 🔒 **Konfiguracja SSL i Custom Domain (opcjonalne)**

### **Dodaj Custom Domain**

```bash
# Kup domenę (np. na Azure, Cloudflare, OVH)
# Dodaj do App Service

az webapp config hostname add \
  --webapp-name sportrental-admin \
  --resource-group SportRental-Production \
  --hostname admin.twojedomena.pl

# Azure automatycznie dodaje darmowy SSL (Let's Encrypt)
```

---

## 📊 **Monitorowanie**

### **1. Application Insights**

```bash
# Utwórz App Insights
az monitor app-insights component create \
  --app sportrental-insights \
  --location westeurope \
  --resource-group SportRental-Production \
  --application-type web

# Pobierz Instrumentation Key
INSIGHTS_KEY=$(az monitor app-insights component show \
  --app sportrental-insights \
  --resource-group SportRental-Production \
  --query instrumentationKey -o tsv)

# Dodaj do App Services
az webapp config appsettings set \
  --name sportrental-admin \
  --resource-group SportRental-Production \
  --settings "APPLICATIONINSIGHTS_CONNECTION_STRING=InstrumentationKey=$INSIGHTS_KEY"
```

### **2. Sprawdź Logi**

```bash
# Live logs
az webapp log tail \
  --name sportrental-admin \
  --resource-group SportRental-Production

# Pobierz ostatnie logi
az webapp log download \
  --name sportrental-admin \
  --resource-group SportRental-Production
```

---

## 🐛 **Troubleshooting**

### **Problem: "Application Error"**

```bash
# Włącz szczegółowe logi
az webapp log config \
  --name sportrental-admin \
  --resource-group SportRental-Production \
  --application-logging filesystem \
  --level verbose

# Sprawdź logi
az webapp log tail --name sportrental-admin --resource-group SportRental-Production
```

### **Problem: Database connection failed**

1. Sprawdź firewall PostgreSQL:
```bash
az postgres flexible-server firewall-rule list \
  --name sportrental-db-prod \
  --resource-group SportRental-Production
```

2. Dodaj IP App Service do firewall:
```bash
OUTBOUND_IPS=$(az webapp show \
  --name sportrental-admin \
  --resource-group SportRental-Production \
  --query outboundIpAddresses -o tsv)

# Dodaj każdy IP jako firewall rule
for ip in $(echo $OUTBOUND_IPS | tr ',' ' '); do
  az postgres flexible-server firewall-rule create \
    --name sportrental-db-prod \
    --resource-group SportRental-Production \
    --rule-name "AppService-$ip" \
    --start-ip-address $ip \
    --end-ip-address $ip
done
```

### **Problem: Key Vault access denied**

```bash
# Sprawdź czy Managed Identity ma permissions
az keyvault show \
  --name <YOUR_KEYVAULT_NAME> \
  --query properties.accessPolicies

# Jeśli nie, dodaj ponownie:
IDENTITY=$(az webapp identity show \
  --name sportrental-admin \
  --resource-group SportRental-Production \
  --query principalId -o tsv)

az keyvault set-policy \
  --name <YOUR_KEYVAULT_NAME> \
  --object-id $IDENTITY \
  --secret-permissions get list
```

---

## 💰 **Optymalizacja Kosztów**

### **1. Auto-scaling (gdy rośnie traffic)**

```bash
az monitor autoscale create \
  --resource-group SportRental-Production \
  --resource sportrental-admin \
  --resource-type Microsoft.Web/serverfarms \
  --name SportRental-AutoScale \
  --min-count 1 \
  --max-count 3 \
  --count 1
```

### **2. Backup (ważne!)**

```bash
# Backup bazy danych (automatyczny)
az postgres flexible-server parameter set \
  --name sportrental-db-prod \
  --resource-group SportRental-Production \
  --name backup_retention_days \
  --value 7
```

---

## ✅ **Checklist Po Deployment**

- [ ] ✅ Wszystkie App Services działają
- [ ] ✅ Baza danych dostępna
- [ ] ✅ Migracje EF Core uruchomione
- [ ] ✅ Managed Identity skonfigurowane
- [ ] ✅ Key Vault dostępny dla wszystkich apps
- [ ] ✅ CORS skonfigurowany
- [ ] ✅ Blob Storage działa
- [ ] ✅ Application Insights skonfigurowany
- [ ] ✅ Custom domain (opcjonalnie)
- [ ] ✅ SSL certyfikaty
- [ ] ✅ Backup skonfigurowany

---

## 🎯 **RZECZYWISTY DEPLOYMENT (Grudzień 2024)**

Poniżej dokładne komendy użyte podczas faktycznego wdrożenia na darmowe plany Azure.

### **Aktualne adresy produkcyjne:**

| Aplikacja | URL | Plan |
|-----------|-----|------|
| **Admin Panel** (Blazor Server) | https://sradmin2.azurewebsites.net | App Service F1 Free |
| **Klient WASM** | https://nice-tree-0359d8403.3.azurestaticapps.net | Static Web App Free |

### **1. Utworzenie darmowego App Service Plan (Poland Central)**

```powershell
# Tworzenie nowego darmowego planu w Polsce dla niskiego pingu
az appservice plan create `
  --name sportrental-free `
  --resource-group DefaultResourceGroup-PLC `
  --location polandcentral `
  --sku F1 `
  --is-linux false
```

### **2. Utworzenie App Service dla Admin Panel**

```powershell
# Utworzenie nowej aplikacji na darmowym planie
az webapp create `
  --name sradmin2 `
  --resource-group DefaultResourceGroup-PLC `
  --plan sportrental-free `
  --runtime "dotnet:10"

# Włączenie Managed Identity
az webapp identity assign `
  --name sradmin2 `
  --resource-group DefaultResourceGroup-PLC

# Pobranie Principal ID
$principalId = az webapp identity show `
  --name sradmin2 `
  --resource-group DefaultResourceGroup-PLC `
  --query principalId -o tsv

# Nadanie uprawnień do Key Vault (RBAC)
az role assignment create `
  --role "Key Vault Secrets User" `
  --assignee $principalId `
  --scope "/subscriptions/<YOUR_SUBSCRIPTION_ID>/resourceGroups/<YOUR_RESOURCE_GROUP>/providers/Microsoft.KeyVault/vaults/<YOUR_KEYVAULT_NAME>"

# Konfiguracja App Settings
az webapp config appsettings set `
  --name sradmin2 `
  --resource-group DefaultResourceGroup-PLC `
  --settings `
    "KeyVault__Url=https://<YOUR_KEYVAULT_NAME>.vault.azure.net/" `
    "ASPNETCORE_ENVIRONMENT=Production" `
    "Storage__Provider=AzureBlob"
```

### **3. Build i Deploy Admin Panel**

```powershell
cd SportRental.Admin

# Build Release
dotnet publish -c Release -o ./publish

# Tworzenie ZIP
cd publish
Compress-Archive -Path * -DestinationPath ../admin.zip -Force
cd ..

# Deploy przez ZIP
az webapp deployment source config-zip `
  --name sradmin2 `
  --resource-group DefaultResourceGroup-PLC `
  --src admin.zip
```

### **4. Utworzenie Azure Static Web App dla WASM**

```powershell
# Utworzenie Static Web App
az staticwebapp create `
  --name srclient-wasm `
  --resource-group DefaultResourceGroup-PLC `
  --location westeurope `
  --sku Free
```

### **5. Build i Deploy WASM Client**

```powershell
cd SportRental.Client

# Build WASM
dotnet publish -c Release -o ./publish

# Deploy przez SWA CLI
$token = az staticwebapp secrets list --name srclient-wasm --query "properties.apiKey" -o tsv
swa deploy "./publish/wwwroot" --deployment-token $token --env production
```

### **6. Konfiguracja CORS**

```powershell
# Dodanie domeny Static Web App do CORS w Admin API
az webapp cors add `
  --name sradmin2 `
  --resource-group DefaultResourceGroup-PLC `
  --allowed-origins "https://nice-tree-0359d8403.3.azurestaticapps.net"
```

### **7. Konfiguracja plików dla WASM**

**`SportRental.Client/wwwroot/appsettings.Production.json`:**
```json
{
  "Api": {
    "BaseUrl": "https://sradmin2.azurewebsites.net",
    "TenantId": "547f5df7-a389-44b3-bcc6-090ff2fa92e5"
  }
}
```

**`SportRental.Client/wwwroot/staticwebapp.config.json`:**
```json
{
  "navigationFallback": {
    "rewrite": "/index.html",
    "exclude": ["/_framework/*", "/css/*", "/*.{css,ico,js,json,png,jpg,svg,woff,woff2}"]
  },
  "mimeTypes": {
    ".wasm": "application/wasm",
    ".dll": "application/octet-stream",
    ".dat": "application/octet-stream",
    ".blat": "application/octet-stream"
  },
  "globalHeaders": {
    "Access-Control-Allow-Origin": "*"
  }
}
```

### **Rozwiązane problemy:**

| Problem | Rozwiązanie |
|---------|-------------|
| Wyczerpany limit CPU na starym planie F1 | Utworzenie nowego planu `sportrental-free` |
| HTTP 500.30 po deploy | Przypisanie roli "Key Vault Secrets User" do Managed Identity |
| 404 na routing SPA w Static Web App | Dodanie `staticwebapp.config.json` z `navigationFallback` |
| CORS blocked | `az webapp cors add` z domeną Static Web App |
| Pliki .wasm nie ładują się | Dodanie `mimeTypes` w config |

### **Ważne uwagi:**

- **Free plan F1** ma limit 60 minut CPU dziennie - przy intensywnym użyciu może się wyczerpać
- **Key Vault z RBAC** wymaga roli "Key Vault Secrets User", nie access policy
- **Static Web App** wymaga pliku `staticwebapp.config.json` dla SPA routing
- **Managed Identity** potrzebuje kilku minut na propagację uprawnień

---

## 📚 **Dodatkowe Zasoby**

- [Azure App Service Docs](https://learn.microsoft.com/azure/app-service/)
- [Azure PostgreSQL Docs](https://learn.microsoft.com/azure/postgresql/)
- [Azure Key Vault Docs](https://learn.microsoft.com/azure/key-vault/)
- [Blazor on Azure](https://learn.microsoft.com/aspnet/core/blazor/host-and-deploy/webassembly)
- [Azure Static Web Apps](https://learn.microsoft.com/azure/static-web-apps/)

---

## 🆘 **Potrzebujesz Pomocy?**

**Contact:** hdtdtr@gmail.com

---

**Powodzenia z deploymentem! 🚀**



