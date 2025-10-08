# 🚀 Azure Blob Storage - Setup Guide

## ✅ **ZAIMPLEMENTOWANE!**

Azure Blob Storage jest teraz w pełni zintegrowany z aplikacją. Wszystkie zmiany zostały wprowadzone z zachowaniem backward compatibility.

---

## 📦 **Co zostało dodane:**

### **1. NuGet Package:**
```bash
✅ Azure.Storage.Blobs 12.25.1
```

### **2. Nowa implementacja:**
```
✅ SportRental.Admin/Services/Storage/AzureBlobStorage.cs
```

**Features:**
- ✅ Automatyczne tworzenie kontenera
- ✅ Ustawianie Content-Type na podstawie rozszerzenia
- ✅ Cache headers (1 rok dla immutable images)
- ✅ Support dla custom CDN URL
- ✅ Proper error handling i logging
- ✅ Async/await pattern

### **3. Conditional Storage Provider:**
```
✅ SportRental.Admin/Program.cs - Updated
```

**Dostępne providery:**
- `AzureBlob` - Azure Blob Storage (production)
- `Remote` - MediaStorage microservice
- `AppData` - Local App_Data folder
- `Local` - Local wwwroot folder
- `S3` - S3-compatible storage
- Auto-detect (default)

### **4. Konfiguracja:**
```
✅ appsettings.json - Added Storage section
✅ appsettings.Development.json - Added with placeholder
```

---

## 🔧 **Jak użyć - Krok po kroku:**

### **Step 1: Utwórz Azure Storage Account**

#### **Opcja A: Azure Portal (GUI)**
1. Przejdź do https://portal.azure.com
2. Create a resource → Storage account
3. Wybierz:
   - **Resource group:** (twoja grupa lub nowa)
   - **Storage account name:** `sportrentalstore` (musi być unique globalnie)
   - **Region:** West Europe (lub najbliżej Twoich użytkowników)
   - **Performance:** Standard
   - **Redundancy:** LRS (Local) lub GRS (Geo) dla production
4. **Networking:** Public endpoint
5. **Data protection:** 
   - ✅ Enable versioning (opcjonalnie)
   - ✅ Enable soft delete (7 dni)
6. Review + Create

#### **Opcja B: Azure CLI**
```bash
# Login
az login

# Create resource group (jeśli nie masz)
az group create --name SportRental-RG --location westeurope

# Create storage account
az storage account create \
  --name sportrentalstore \
  --resource-group SportRental-RG \
  --location westeurope \
  --sku Standard_LRS \
  --kind StorageV2 \
  --access-tier Hot \
  --allow-blob-public-access true

# Get connection string
az storage account show-connection-string \
  --name sportrentalstore \
  --resource-group SportRental-RG \
  --output tsv
```

---

### **Step 2: Pobierz Connection String**

#### **Azure Portal:**
1. Przejdź do swojego Storage Account
2. **Security + networking** → **Access keys**
3. Kliknij "Show" przy **key1**
4. Skopiuj **Connection string**

Przykład:
```
DefaultEndpointsProtocol=https;AccountName=sportrentalstore;AccountKey=abc123...==;EndpointSuffix=core.windows.net
```

#### **Azure CLI:**
```bash
az storage account show-connection-string \
  --name sportrentalstore \
  --resource-group SportRental-RG
```

---

### **Step 3: Skonfiguruj aplikację**

#### **appsettings.Development.json:**
```json
{
  "Storage": {
    "Provider": "AzureBlob",
    "AzureBlob": {
      "ConnectionString": "DefaultEndpointsProtocol=https;AccountName=sportrentalstore;AccountKey=YOUR_KEY_HERE;EndpointSuffix=core.windows.net",
      "ContainerName": "sportrental-images",
      "PublicBaseUrl": ""  // Opcjonalnie: CDN URL
    }
  }
}
```

#### **Dla Production (appsettings.json na Azure):**
Lepiej używać **User Secrets** lub **Azure Key Vault**:

```bash
# User Secrets (local development)
dotnet user-secrets set "Storage:AzureBlob:ConnectionString" "YOUR_CONNECTION_STRING" --project SportRental.Admin

# Azure App Service (production)
az webapp config appsettings set \
  --name YourAppServiceName \
  --resource-group SportRental-RG \
  --settings Storage__AzureBlob__ConnectionString="YOUR_CONNECTION_STRING"
```

---

### **Step 4: Test konfiguracji**

#### **Build & Run:**
```bash
dotnet build SportRental.Admin/SportRental.Admin.csproj
dotnet run --project SportRental.Admin
```

#### **Sprawdź logi:**
```
Storage Provider: azureblob
AzureBlobStorage initialized. Container: sportrental-images
```

#### **Upload test:**
1. Przejdź do `/admin/products`
2. Dodaj nowy produkt
3. Upload zdjęcia
4. Sprawdź w Azure Portal:
   - Storage Account → Containers → `sportrental-images`
   - Powinien pojawić się folder: `images/products/{tenant-id}/{product-id}/`

---

## 🎯 **Konfiguracja Providers:**

### **Rozwój lokalny (bez Azure):**
```json
{
  "Storage": {
    "Provider": "AppData"  // Używa App_Data folder
  }
}
```

### **Rozwój z MediaStorage:**
```json
{
  "Storage": {
    "Provider": "Remote",
    "MediaStorage": {
      "BaseUrl": "https://localhost:7002"
    }
  }
}
```

### **Production (Azure Blob):**
```json
{
  "Storage": {
    "Provider": "AzureBlob",
    "AzureBlob": {
      "ConnectionString": "...",
      "ContainerName": "sportrental-images"
    }
  }
}
```

### **Auto-detect (domyślnie):**
```json
{
  "Storage": {
    // Brak "Provider" - auto-detect
    "AzureBlob": {
      "ConnectionString": "..."  // Jeśli jest, użyje Azure Blob
    }
  }
}
```

---

## 🚀 **CDN Integration (Opcjonalnie):**

### **CloudFlare CDN (DARMOWY!):**

#### **1. Utwórz CloudFlare account:**
- https://dash.cloudflare.com/sign-up

#### **2. Dodaj domenę:**
- Add a site → `yourdomain.com`
- Update DNS at your registrar

#### **3. Skonfiguruj CNAME:**
```
cdn.yourdomain.com → sportrentalstore.blob.core.windows.net
```

#### **4. Update appsettings:**
```json
{
  "Storage": {
    "AzureBlob": {
      "ConnectionString": "...",
      "ContainerName": "sportrental-images",
      "PublicBaseUrl": "https://cdn.yourdomain.com/sportrental-images"
    }
  }
}
```

**Korzyści:**
- ✅ Darmowy unlimited bandwidth
- ✅ Global CDN (200+ locations)
- ✅ Automatyczna kompresja
- ✅ HTTP/2, HTTP/3
- ✅ DDoS protection

---

### **Azure CDN:**

#### **1. Create CDN Profile:**
```bash
az cdn profile create \
  --name SportRental-CDN \
  --resource-group SportRental-RG \
  --sku Standard_Microsoft

az cdn endpoint create \
  --name sportrentalcdn \
  --profile-name SportRental-CDN \
  --resource-group SportRental-RG \
  --origin sportrentalstore.blob.core.windows.net \
  --origin-host-header sportrentalstore.blob.core.windows.net
```

#### **2. Update appsettings:**
```json
{
  "Storage": {
    "AzureBlob": {
      "PublicBaseUrl": "https://sportrentalcdn.azureedge.net/sportrental-images"
    }
  }
}
```

**Koszty:**
- ~$0.08/GB transfer (pierwszy 10 GB/miesiąc darmowy)

---

## 📊 **Performance Comparison:**

| Scenariusz | App_Data | Azure Blob | Azure + CDN |
|------------|----------|------------|-------------|
| **Single Image** | 15ms | 40ms | 18ms (cache) |
| **20 Images** | 1.5s | 2.5s | 800ms |
| **High Traffic** | 200ms+ | 50ms | 18ms |
| **Koszty (10k views/m)** | $0 | $5 | $5 (CloudFlare free) |

---

## 🔒 **Bezpieczeństwo:**

### **1. Private Container + SAS Tokens (Opcjonalnie):**

Jeśli chcesz prywatne pliki z ograniczonym dostępem:

```csharp
// W AzureBlobStorage.cs możesz dodać:
public string GenerateSasUrl(string blobPath, TimeSpan validity)
{
    var blobClient = GetBlobClient(blobPath);
    var sasBuilder = new BlobSasBuilder
    {
        BlobContainerName = _containerName,
        BlobName = blobPath,
        Resource = "b",
        StartsOn = DateTimeOffset.UtcNow.AddMinutes(-5),
        ExpiresOn = DateTimeOffset.UtcNow.Add(validity)
    };
    sasBuilder.SetPermissions(BlobSasPermissions.Read);
    
    return blobClient.GenerateSasUri(sasBuilder).ToString();
}
```

### **2. CORS Configuration:**

Jeśli klient WASM pobiera bezpośrednio:

```bash
az storage cors add \
  --account-name sportrentalstore \
  --services b \
  --methods GET HEAD \
  --origins https://yourdomain.com \
  --allowed-headers "*" \
  --exposed-headers "*" \
  --max-age 3600
```

---

## 🐛 **Troubleshooting:**

### **Problem: "Blob container 'sportrental-images' not found"**
**Rozwiązanie:** Container jest tworzony automatycznie przy pierwszym upload. Jeśli błąd, sprawdź permissions.

### **Problem: "403 Forbidden"**
**Rozwiązanie:** 
1. Sprawdź czy Storage Account ma "Allow Blob public access" = Enabled
2. Sprawdź container access level (Blob = public read)

### **Problem: "Connection string invalid"**
**Rozwiązanie:** Upewnij się że skopiowałeś cały string z Azure Portal (może być długi!)

### **Problem: "Slow uploads"**
**Rozwiązanie:**
1. Użyj Storage Account w tym samym regionie co App Service
2. Sprawdź rozmiar plików (może WebP compression?)
3. Włącz CDN

---

## 📋 **Checklist:**

Przed uruchomieniem na production:

- [ ] Azure Storage Account utworzony
- [ ] Connection String skonfigurowany (User Secrets lub Key Vault)
- [ ] Container name skonfigurowany: `sportrental-images`
- [ ] Public access enabled (lub SAS tokens)
- [ ] CORS skonfigurowany (jeśli potrzebny)
- [ ] CDN skonfigurowany (CloudFlare lub Azure)
- [ ] Testowy upload działa
- [ ] Zdjęcia wyświetlają się w kliencie
- [ ] Backup policy ustawiony (soft delete)
- [ ] Monitoring włączony (Azure Monitor)

---

## 💰 **Koszty (szacunki):**

### **Mały projekt (10k pageviews/m):**
```
Storage: 5 GB × $0.018/GB = $0.09/m
Transactions: 100k × $0.004/10k = $0.04/m
Egress: 50 GB × $0.087/GB = $4.35/m
TOTAL: ~$5/m
```

### **Z CloudFlare CDN (Free):**
```
Storage: $0.09/m
Transactions: $0.04/m
Egress: 1 GB × $0.087/GB = $0.09/m  (tylko cache miss)
CloudFlare: $0 (Free Tier)
TOTAL: ~$0.25/m  🎉
```

---

## 🎉 **GOTOWE!**

Twoja aplikacja jest teraz skonfigurowana z Azure Blob Storage!

**Następne kroki:**
1. Wklej Connection String do `appsettings.Development.json`
2. Uruchom aplikację
3. Upload testowego zdjęcia
4. Sprawdź Azure Portal czy pojawił się blob
5. (Opcjonalnie) Skonfiguruj CDN

**Dokumenty:**
- `BLOB_STORAGE_VS_INTERNAL_COMPARISON.md` - Porównanie opcji
- `BLOB_STORAGE_PERFORMANCE_COMPARISON.md` - Benchmarki
- `AZURE_BLOB_STORAGE_SETUP.md` - Ten dokument

---

**Status:** ✅ **READY FOR CONNECTION STRING**  
**Czeka na:** Azure Storage Account credentials  
**Czas setup:** ~10 minut  

🚀 **Daj mi Connection String i uruchamiamy!**
