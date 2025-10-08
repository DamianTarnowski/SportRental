# 🔐 Azure Key Vault - Configuration Guide

## 📋 Przegląd

Aplikacja SportRental została skonfigurowana aby **zawsze używać Azure Key Vault** do zarządzania sekretami - lokalnie, dev, staging i production. **Kod jest identyczny we wszystkich środowiskach.**

### ✨ Kluczowe funkcje:

✅ **DefaultAzureCredential** - automatyczne wykrywanie credentials:
- **Lokalnie:** `az login` (Azure CLI) ← **UŻYWANE LOKALNIE!**
- **Dev/Staging/Prod:** Managed Identity
- **Fallback:** Visual Studio, Environment Variables, Service Principal

✅ **Jedna prawda** - wszystkie sekrety w Key Vault (zero w plikach!)  
✅ **Kod identyczny** - local/dev/staging/prod używają tego samego mechanizmu  
✅ **Per-environment Key Vaults** - izolacja sekretów per środowisko  
✅ **No code changes** - tylko `KeyVault:Url` w appsettings per environment  

---

## ⚡ Quick Start (5 minut)

Masz już Key Vault na Azure? Szybki setup:

```bash
# 1. Zaloguj się
az login

# 2. Dodaj swój email do Key Vault permissions
az keyvault set-policy \
  --name YOUR-KEYVAULT-NAME \
  --upn your-email@company.com \
  --secret-permissions get list

# 3. Test dostępu
az keyvault secret list --vault-name YOUR-KEYVAULT-NAME

# 4. Dodaj URL do appsettings.Development.json
# "KeyVault": { "Url": "https://YOUR-KEYVAULT-NAME.vault.azure.net/" }

# 5. Uruchom
dotnet run --project SportRental.Admin

# Musisz zobaczyć:
# 🔐 Azure Key Vault configured: https://...
```

✅ **Działa?** Gratulacje! Wszystkie sekrety są teraz z Key Vault!  
❌ **Błąd?** Sprawdź [Troubleshooting](#🔍-troubleshooting) poniżej.

---

## 🚀 Jak to działa?

### **Architecture:**

```
┌─────────────────────────────────────────────────────┐
│                                                     │
│  Program.cs (Startup)                               │
│                                                     │
│  1. Check: KeyVault:Url in appsettings.json        │
│  2. If empty → Use local secrets (appsettings)     │
│  3. If set → Connect to Azure Key Vault            │
│  4. Use DefaultAzureCredential                      │
│                                                     │
│     ┌─────────────────────────────────────┐        │
│     │ DefaultAzureCredential (priority):   │        │
│     │ 1. Azure CLI (az login) ← LOCAL     │        │
│     │ 2. Managed Identity ← AZURE         │        │
│     │ 3. Visual Studio credentials         │        │
│     │ 4. Environment Variables             │        │
│     │ 5. Shared Token Cache                │        │
│     └─────────────────────────────────────┘        │
│                                                     │
└─────────────────────────────────────────────────────┘
```

### **Local Development (BEST PRACTICE):**
```json
{
  "KeyVault": {
    "Url": "https://kv-sportrental-dev.vault.azure.net/"
  }
  // ✅ az login → Azure CLI credentials
  // ✅ DefaultAzureCredential picks it up automatically
  // ✅ Wszystkie sekrety z Key Vault
}
```

**Terminal:**
```bash
az login
dotnet run --project SportRental.Admin

# Output:
# 🔐 Azure Key Vault configured: https://kv-sportrental-dev.vault.azure.net/
# ✅ Connection to database: OK
# ✅ Stripe credentials loaded from Key Vault
```

### **Production (Azure App Service):**
```json
{
  "KeyVault": {
    "Url": "https://kv-sportrental-prod.vault.azure.net/"
  }
  // ✅ Managed Identity → automatic authentication
  // ✅ Wszystkie sekrety z Key Vault
  // ✅ Kod IDENTYCZNY jak lokalnie!
}
```

---

## 📦 Co zostało dodane?

### **1. NuGet Packages:**

Dodane do `SportRental.Admin` i `SportRental.Api`:
```xml
<PackageReference Include="Azure.Identity" Version="1.16.0" />
<PackageReference Include="Azure.Extensions.AspNetCore.Configuration.Secrets" Version="1.4.0" />
```

### **2. Program.cs (obie aplikacje):**

```csharp
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Azure.Extensions.AspNetCore.Configuration.Secrets;

var builder = WebApplication.CreateBuilder(args);

// Azure Key Vault Configuration
var keyVaultUrl = builder.Configuration["KeyVault:Url"];
if (!string.IsNullOrWhiteSpace(keyVaultUrl))
{
    var secretClient = new SecretClient(new Uri(keyVaultUrl), new DefaultAzureCredential());
    builder.Configuration.AddAzureKeyVault(secretClient, new KeyVaultSecretManager());
    builder.Services.AddSingleton(_ => secretClient);
    
    var logger = LoggerFactory.Create(b => b.AddConsole()).CreateLogger("Startup");
    logger.LogInformation("🔐 Azure Key Vault configured: {KeyVaultUrl}", keyVaultUrl);
}
```

### **3. appsettings.json:**

Dodana sekcja `KeyVault`:
```json
{
  "KeyVault": {
    "Url": ""
  }
}
```

---

## 🛠️ Setup - Krok po kroku

### **Krok 1: Utwórz Azure Key Vault (per środowisko)**

**BEST PRACTICE: Osobny Key Vault dla każdego środowiska!**

```bash
# Zaloguj się do Azure
az login

# Utwórz Resource Group (jeśli nie istnieje)
az group create --name rg-sportrental --location westeurope

# DEV Key Vault (dla local development + dev environment)
az keyvault create \
  --name kv-sportrental-dev \
  --resource-group rg-sportrental \
  --location westeurope \
  --enable-rbac-authorization false

# STAGING Key Vault (opcjonalnie)
az keyvault create \
  --name kv-sportrental-staging \
  --resource-group rg-sportrental \
  --location westeurope \
  --enable-rbac-authorization false

# PRODUCTION Key Vault
az keyvault create \
  --name kv-sportrental-prod \
  --resource-group rg-sportrental \
  --location westeurope \
  --enable-rbac-authorization false

# Zapisz URLs:
# Dev:     https://kv-sportrental-dev.vault.azure.net/
# Staging: https://kv-sportrental-staging.vault.azure.net/
# Prod:    https://kv-sportrental-prod.vault.azure.net/
```

**Naming conventions:**
- Nazwa Key Vault musi być **globalnie unikalna**
- Max 24 znaki, tylko alfanumeryczne i `-`
- Przykłady: `kv-sportrental-dev`, `kv-sr-staging`, `kv-sr-prod`

**💡 Dlaczego osobne Key Vaults?**
- ✅ **Izolacja** - dev nie ma dostępu do prod secrets
- ✅ **Security** - różne uprawnienia per environment
- ✅ **Testing** - możesz używać test credentials w dev
- ✅ **Compliance** - wymagane w wielu standardach (PCI-DSS, SOC2)

---

### **Krok 2: Dodaj sekrety do Key Vault**

**WAŻNE:** Azure Key Vault **nie wspiera nested JSON** ani `:` w nazwach sekretów.

**Konwencje nazewnicze:**
```
appsettings.json:           Azure Key Vault:
ConnectionStrings:          ConnectionStrings--DefaultConnection
  DefaultConnection
  
Stripe:SecretKey       →    Stripe--SecretKey
Email:Smtp:Password    →    Email--Smtp--Password
Jwt:SigningKey         →    Jwt--SigningKey
```

**Przykład - dodawanie sekretów:**

```bash
# Connection String
az keyvault secret set \
  --vault-name kv-sportrental-prod \
  --name "ConnectionStrings--DefaultConnection" \
  --value "Host=mydb.postgres.database.azure.com;Port=5432;Database=sr;Username=admin;Password=SuperSecret123!;SSL Mode=Require"

# Stripe Secret Key
az keyvault secret set \
  --vault-name kv-sportrental-prod \
  --name "Stripe--SecretKey" \
  --value "sk_live_..."

# Stripe Publishable Key
az keyvault secret set \
  --vault-name kv-sportrental-prod \
  --name "Stripe--PublishableKey" \
  --value "pk_live_..."

# Stripe Webhook Secret
az keyvault secret set \
  --vault-name kv-sportrental-prod \
  --name "Stripe--WebhookSecret" \
  --value "whsec_..."

# JWT Signing Key
az keyvault secret set \
  --vault-name kv-sportrental-prod \
  --name "Jwt--SigningKey" \
  --value "your-production-secret-key-min-32-chars-super-secure!"

# Email SMTP Password (Onet)
az keyvault secret set \
  --vault-name kv-sportrental-prod \
  --name "Email--Smtp--Password" \
  --value "your-onet-password"

# Email SMTP Username
az keyvault secret set \
  --vault-name kv-sportrental-prod \
  --name "Email--Smtp--Username" \
  --value "contact.sportrental@op.pl"

# Azure Blob Storage Connection String
az keyvault secret set \
  --vault-name kv-sportrental-prod \
  --name "Storage--AzureBlob--ConnectionString" \
  --value "DefaultEndpointsProtocol=https;AccountName=nowyblob;AccountKey=..."
```

**💡 Pro tip:** Użyj PowerShell lub skryptu aby dodać wszystkie sekrety naraz:

```powershell
$vaultName = "kv-sportrental-prod"
$secrets = @{
    "ConnectionStrings--DefaultConnection" = "Host=..."
    "Stripe--SecretKey" = "sk_live_..."
    "Stripe--PublishableKey" = "pk_live_..."
    "Jwt--SigningKey" = "your-secret-key"
    # ... inne sekrety
}

foreach ($key in $secrets.Keys) {
    az keyvault secret set --vault-name $vaultName --name $key --value $secrets[$key]
    Write-Host "✅ Added: $key"
}
```

---

### **Krok 3: Nadaj uprawnienia**

#### **A. Local Development (az login):**

```bash
# Sprawdź kto jest zalogowany
az account show

# Nadaj sobie uprawnienia do Key Vault
az keyvault set-policy \
  --name kv-sportrental-prod \
  --upn "your-email@company.com" \
  --secret-permissions get list

# ALBO użyj Object ID
az keyvault set-policy \
  --name kv-sportrental-prod \
  --object-id "your-azure-ad-object-id" \
  --secret-permissions get list
```

#### **B. Azure App Service (Managed Identity):**

Dla produkcji w Azure App Service:

```bash
# 1. Włącz System-Assigned Managed Identity w App Service
az webapp identity assign \
  --name your-app-service-name \
  --resource-group rg-sportrental

# Output: principalId (skopiuj to)

# 2. Nadaj App Service dostęp do Key Vault
az keyvault set-policy \
  --name kv-sportrental-prod \
  --object-id "principal-id-from-step-1" \
  --secret-permissions get list
```

**Alternatywnie w Azure Portal:**
1. App Service → Identity → System assigned → **On**
2. Key Vault → Access policies → Add Access Policy
   - Secret permissions: **Get**, **List**
   - Select principal: **your-app-service-name**

---

### **Krok 4: Skonfiguruj aplikację**

#### **Local Development (BEST PRACTICE):**

**appsettings.Development.json:**
```json
{
  "KeyVault": {
    "Url": "https://kv-sportrental-dev.vault.azure.net/"
  }
  // ✅ Lokalnie: az login → automatyczne uwierzytelnienie
  // ✅ Dev/Staging/Prod: Managed Identity
  // ✅ Kod ten sam wszędzie!
}
```

**💡 Dlaczego to jest lepsze?**
- ✅ **Jedna prawda** - wszystkie sekrety w Key Vault
- ✅ **Kod identyczny** - dev/staging/prod używają tego samego mechanizmu
- ✅ **Bezpieczeństwo** - zero sekretów w plikach
- ✅ **Audit** - widzisz kto, kiedy, jakie sekrety odczytał
- ✅ **Rotacja** - zmiana sekretu w jednym miejscu dla wszystkich

#### **Production (Azure App Service):**

**appsettings.json:**
```json
{
  "KeyVault": {
    "Url": "https://kv-sportrental-prod.vault.azure.net/"
  }
}
```

**ALBO** lepiej - użyj **App Settings** w Azure Portal:
- App Service → Configuration → Application settings
- Dodaj: `KeyVault__Url` = `https://kv-sportrental-prod.vault.azure.net/`

**💡 Dlaczego App Settings?**
- Nie commitujesz URL do repo
- Łatwa zmiana per environment (dev, staging, prod)
- Override appsettings.json

---

### **Konfiguracja per środowisko (RECOMMENDED):**

#### **Struktura plików:**

```
appsettings.json                    ← Base config (NIE commitować secrets!)
appsettings.Development.json        ← Local dev (kv-sportrental-dev)
appsettings.Staging.json            ← Staging (kv-sportrental-staging)
appsettings.Production.json         ← Production (kv-sportrental-prod)
```

#### **appsettings.json (base - commitowany do repo):**

```json
{
  "KeyVault": {
    "Url": ""  
  }
  // Puste - będzie overridden przez environment-specific files
}
```

#### **appsettings.Development.json (local + dev environment):**

```json
{
  "KeyVault": {
    "Url": "https://kv-sportrental-dev.vault.azure.net/"
  }
}
```

#### **appsettings.Staging.json:**

```json
{
  "KeyVault": {
    "Url": "https://kv-sportrental-staging.vault.azure.net/"
  }
}
```

#### **appsettings.Production.json:**

```json
{
  "KeyVault": {
    "Url": "https://kv-sportrental-prod.vault.azure.net/"
  }
}
```

**💡 Jak to działa?**
- Lokalnie: `ASPNETCORE_ENVIRONMENT=Development` → używa `appsettings.Development.json`
- W Azure: ustawiasz `ASPNETCORE_ENVIRONMENT` w App Settings
- Kod ten sam → Key Vault URL różny per environment

**⚠️ WAŻNE:** Dodaj do `.gitignore`:
```
appsettings.Development.json
appsettings.Staging.json
appsettings.Production.json
```
ALBO commituj je BEZ sekretów (tylko Key Vault URL)

---

## 🧪 Testowanie

### **Test lokalny (RECOMMENDED SETUP):**

```bash
# 1. Zaloguj się do Azure
az login

# 2. Sprawdź czy masz dostęp do Key Vault
az keyvault secret list --vault-name kv-sportrental-dev

# 3. Ustaw environment variable (opcjonalnie)
$env:ASPNETCORE_ENVIRONMENT = "Development"

# 4. Uruchom aplikację
dotnet run --project SportRental.Admin

# W logach MUSISZ zobaczyć:
# 🔐 Azure Key Vault configured: https://kv-sportrental-dev.vault.azure.net/
```

**✅ Jeśli widzisz log:**
- Key Vault działa!
- DefaultAzureCredential użył `az login`
- Sekrety są odczytywane z Key Vault

**❌ Jeśli NIE widzisz logu:**
- `KeyVault:Url` jest puste w `appsettings.Development.json`
- ALBO nie jesteś zalogowany (`az login`)
- ALBO nie masz uprawnień do Key Vault

### **Test connection stringa:**

```bash
# Sprawdź czy aplikacja używa connection string z Key Vault
dotnet run --project SportRental.Admin

# Jeśli connection string jest z Key Vault:
# ✅ Połączenie do bazy powinno działać
# ✅ Nie zobaczysz błędów "Connection string not found"
```

### **3. Test w Azure:**

```bash
# Deploy aplikacji do Azure App Service
az webapp deployment source config-zip \
  --resource-group rg-sportrental \
  --name your-app-service-name \
  --src publish.zip

# Sprawdź logi
az webapp log tail \
  --resource-group rg-sportrental \
  --name your-app-service-name

# Powinien zobaczyć: "🔐 Azure Key Vault configured"
```

---

## 🔍 Troubleshooting

### **Problem: "Azure.Identity: DefaultAzureCredentialcredentialUnavailableException"**

**Przyczyna:** Nie jesteś zalogowany lokalnie ani nie ma Managed Identity.

**Rozwiązanie:**
```bash
# Zaloguj się do Azure
az login

# Sprawdź czy działa
az account show
```

---

### **Problem: "Azure.RequestFailedException: Access denied"**

**Przyczyna:** Brak uprawnień do Key Vault.

**Rozwiązanie:**
```bash
# Sprawdź uprawnienia
az keyvault show --name kv-sportrental-prod --query properties.accessPolicies

# Dodaj uprawnienia
az keyvault set-policy \
  --name kv-sportrental-prod \
  --upn "your-email@company.com" \
  --secret-permissions get list
```

---

### **Problem: "Configuration value is null"**

**Przyczyna:** Sekret nie istnieje w Key Vault lub ma złą nazwę.

**Rozwiązanie:**
```bash
# Lista wszystkich sekretów
az keyvault secret list --vault-name kv-sportrental-prod

# Sprawdź konkretny sekret
az keyvault secret show \
  --vault-name kv-sportrental-prod \
  --name "ConnectionStrings--DefaultConnection"

# Jeśli używasz : zamiast -- → BŁĄD!
# ❌ "ConnectionStrings:DefaultConnection"
# ✅ "ConnectionStrings--DefaultConnection"
```

---

### **Problem: Aplikacja używa local secrets zamiast Key Vault**

**Przyczyna:** `KeyVault:Url` jest puste lub nieprawidłowe.

**Rozwiązanie:**
```bash
# Sprawdź konfigurację
cat appsettings.json | grep -A 3 KeyVault

# Upewnij się że URL jest poprawny
# ✅ "https://kv-sportrental-prod.vault.azure.net/"
# ❌ "https://kv-sportrental-prod.vault.azure.net"  (brak slash)
# ❌ "kv-sportrental-prod"  (brak https://)
```

---

## 📊 Secret Name Mapping

Dla ułatwienia - pełna mapa sekretów:

| appsettings.json | Azure Key Vault Secret Name |
|------------------|------------------------------|
| `ConnectionStrings:DefaultConnection` | `ConnectionStrings--DefaultConnection` |
| `Stripe:SecretKey` | `Stripe--SecretKey` |
| `Stripe:PublishableKey` | `Stripe--PublishableKey` |
| `Stripe:WebhookSecret` | `Stripe--WebhookSecret` |
| `Jwt:SigningKey` | `Jwt--SigningKey` |
| `Email:Smtp:Username` | `Email--Smtp--Username` |
| `Email:Smtp:Password` | `Email--Smtp--Password` |
| `Storage:AzureBlob:ConnectionString` | `Storage--AzureBlob--ConnectionString` |
| `SmsApi:Token` | `SmsApi--Token` |
| `MediaStorage:ApiKey` | `MediaStorage--ApiKey` |

---

## 🔐 Security Best Practices

### **1. Nigdy nie commituj sekretów do repo:**

✅ **DO:**
- Używaj Key Vault w production
- Używaj `user-secrets` lokalnie dla sensitive data
- Używaj environment variables
- Dodaj `appsettings.Production.json` do `.gitignore`

❌ **DON'T:**
- Nie commituj `appsettings.json` z hasłami
- Nie używaj hardcoded secrets w kodzie
- Nie loguj sekretów

### **2. Użyj .NET User Secrets dla local development:**

```bash
# Inicjalizuj user-secrets
cd SportRental.Admin
dotnet user-secrets init

# Dodaj sekrety (NIE pójdą do repo!)
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;..."
dotnet user-secrets set "Stripe:SecretKey" "sk_test_..."

# Lista sekretów
dotnet user-secrets list

# Usuń
dotnet user-secrets clear
```

**Zalety:**
- Nie commitowane do repo
- Per-developer settings
- Override appsettings.json

### **3. Rotacja sekretów:**

```bash
# Utwórz nową wersję sekretu (old version preserved)
az keyvault secret set \
  --vault-name kv-sportrental-prod \
  --name "ConnectionStrings--DefaultConnection" \
  --value "new-connection-string"

# Aplikacja automatycznie użyje najnowszej wersji
# Restart aplikacji może być wymagany
```

### **4. Monitoring:**

```bash
# Włącz diagnostykę Key Vault
az monitor diagnostic-settings create \
  --name key-vault-diagnostics \
  --resource /subscriptions/YOUR-SUBSCRIPTION-ID/resourceGroups/rg-sportrental/providers/Microsoft.KeyVault/vaults/kv-sportrental-prod \
  --logs '[{"category": "AuditEvent", "enabled": true}]' \
  --workspace YOUR-LOG-ANALYTICS-WORKSPACE-ID
```

---

## 🎯 Co zostało zrobione?

✅ **Dodane NuGet packages:**
- `Azure.Identity` (1.16.0)
- `Azure.Extensions.AspNetCore.Configuration.Secrets` (1.4.0)

✅ **Program.cs (SportRental.Admin + SportRental.Api):**
- DefaultAzureCredential z automatycznym fallbackiem
- Conditional Key Vault loading
- Logging dla debugowania

✅ **appsettings.json:**
- `KeyVault:Url` configuration

✅ **Zero code changes required:**
- Lokalnie: puste URL → używa local secrets
- W Azure: ustawiony URL → używa Key Vault
- Seamless transition

---

## 🚀 Deployment Checklist

Przed deploymentem do Azure:

- [ ] Utwórz Azure Key Vault
- [ ] Dodaj wszystkie sekrety z `--` separatorem
- [ ] Włącz Managed Identity w App Service
- [ ] Nadaj App Service uprawnienia do Key Vault (Get, List)
- [ ] Ustaw `KeyVault__Url` w App Service Configuration
- [ ] Usuń sekrety z `appsettings.json` (zostaw puste)
- [ ] Deploy aplikacji
- [ ] Sprawdź logi: "🔐 Azure Key Vault configured"
- [ ] Test połączenia do bazy danych
- [ ] Test Stripe integration

---

## 📚 Related Documentation

- [Azure Key Vault Documentation](https://learn.microsoft.com/azure/key-vault/)
- [DefaultAzureCredential](https://learn.microsoft.com/dotnet/api/azure.identity.defaultazurecredential)
- [.NET User Secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets)
- [App Service Managed Identity](https://learn.microsoft.com/azure/app-service/overview-managed-identity)

---

**Last Updated:** October 7, 2025  
**Status:** ✅ Production Ready
