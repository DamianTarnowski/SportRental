# Instrukcja publikowania aplikacji SportRentalHybrid na Azure

> **Ostatnia aktualizacja:** Grudzień 2025

## Przegląd architektury

Projekt składa się z dwóch **aktywnych** aplikacji:
1. **SportRental.Admin** - Blazor Server (panel administracyjny + **API dla klienta WASM**) → Azure App Service
2. **SportRental.Client** - Blazor WASM (aplikacja kliencka) → Azure Static Web Apps

### ⚠️ Wyłączone projekty
- **SportRental.Api** - wyłączony, API hostowane w Admin
- **SportRental.MediaStorage** - wyłączony, pliki w Azure Blob Storage

---

## 1. Publikacja Admin Panel (Blazor Server) na Azure App Service

### Wymagania wstępne
- Azure CLI zainstalowane i zalogowane (`az login`)
- Istniejący App Service: `sradmin2` w resource group `DefaultResourceGroup-PLC`

### Kroki publikacji

#### Krok 1: Build projektu
```powershell
dotnet build SportRental.Admin --no-restore
```

#### Krok 2: Publikacja do folderu
```powershell
dotnet publish SportRental.Admin -c Release -o ./publish/admin
```

#### Krok 3: Utworzenie archiwum ZIP
```powershell
Compress-Archive -Path "./publish/admin/*" -DestinationPath "./publish/admin.zip" -Force
```

#### Krok 4: Deploy na Azure App Service
```powershell
az webapp deployment source config-zip --resource-group DefaultResourceGroup-PLC --name sradmin2 --src ./publish/admin.zip
```

#### Krok 5: Restart aplikacji (opcjonalnie, dla pewności)
```powershell
az webapp restart --resource-group DefaultResourceGroup-PLC --name sradmin2
```

### Pełna komenda jednolinijkowa
```powershell
dotnet publish SportRental.Admin -c Release -o ./publish/admin; Compress-Archive -Path "./publish/admin/*" -DestinationPath "./publish/admin.zip" -Force; az webapp deployment source config-zip --resource-group DefaultResourceGroup-PLC --name sradmin2 --src ./publish/admin.zip
```

### URL po deploy
- **Produkcja:** https://sradmin2.azurewebsites.net

---

## 2. Publikacja Client (Blazor WASM) na Azure Static Web Apps

### Wymagania wstępne
- Azure Static Web Apps CLI: `npm install -g @azure/static-web-apps-cli`
- Istniejąca Static Web App: `srclient-wasm`
- Deployment token (zapisany w `secrets/azure-secrets.json`)

### Kroki publikacji

#### Krok 1: Build projektu
```powershell
dotnet build SportRental.Client --no-restore
```

#### Krok 2: Publikacja do folderu
```powershell
dotnet publish SportRental.Client -c Release -o ./publish/client
```

#### Krok 3: Deploy na Azure Static Web Apps
```powershell
swa deploy ./publish/client/wwwroot --deployment-token <TOKEN> --env production
```

### Pobranie deployment token (jeśli nie masz)
```powershell
az staticwebapp secrets list --name srclient-wasm --resource-group DefaultResourceGroup-PLC --query "properties.apiKey" -o tsv
```

### Pełna komenda jednolinijkowa
```powershell
dotnet publish SportRental.Client -c Release -o ./publish/client; swa deploy ./publish/client/wwwroot --deployment-token <TOKEN> --env production
```

### URL po deploy
- **Produkcja:** https://nice-tree-0359d8403.3.azurestaticapps.net

---

## 3. Publikacja obu aplikacji naraz

```powershell
# Admin
dotnet publish SportRental.Admin -c Release -o ./publish/admin
Compress-Archive -Path "./publish/admin/*" -DestinationPath "./publish/admin.zip" -Force
az webapp deployment source config-zip --resource-group DefaultResourceGroup-PLC --name sradmin2 --src ./publish/admin.zip

# Client
dotnet publish SportRental.Client -c Release -o ./publish/client
swa deploy ./publish/client/wwwroot --deployment-token <TOKEN> --env production
```

---

## 4. Konfiguracja Azure (już wykonana)

### App Service (sradmin2)
| Parametr | Wartość |
|----------|---------|
| Plan | `sportrental-free` (F1 Free) |
| Region | Poland Central |
| Runtime | .NET 10 |

### Connection String w Azure Portal
- **Name:** `DefaultConnection`
- **Value:** Zobacz `secrets/azure-secrets.json`
- **Type:** `Custom`

### Static Web App (srclient-wasm)
| Parametr | Wartość |
|----------|---------|
| Plan | Free |
| Region | West Europe (CDN) |

### Plik konfiguracyjny SWA
`SportRental.Client/wwwroot/staticwebapp.config.json`:
```json
{
  "navigationFallback": {
    "rewrite": "/index.html",
    "exclude": ["/css/*", "/js/*", "/_framework/*", "/images/*", "/*.json"]
  }
}
```

### CORS (w sradmin2)
Skonfigurowany dla Static Web App:
- `https://nice-tree-0359d8403.3.azurestaticapps.net`

---

## 5. Troubleshooting

### Problem: Aplikacja nie startuje po deploy
```powershell
# Sprawdź logi
az webapp log tail --resource-group DefaultResourceGroup-PLC --name sradmin2

# Restart
az webapp restart --resource-group DefaultResourceGroup-PLC --name sradmin2
```

### Problem: 500 Internal Server Error
- Sprawdź czy connection string jest ustawiony w Azure Portal
- Sprawdź czy Key Vault ma właściwe uprawnienia (Managed Identity)

### Problem: CORS błędy na kliencie
```powershell
az webapp cors add --resource-group DefaultResourceGroup-PLC --name sradmin2 --allowed-origins "https://nice-tree-0359d8403.3.azurestaticapps.net"
```

### Problem: Static Web App zwraca 404
- Upewnij się że `staticwebapp.config.json` jest w `wwwroot`
- Sprawdź czy publikujesz z `./publish/client/wwwroot` (nie `./publish/client`)

---

## 6. Zasoby Azure

| Zasób | Nazwa | Resource Group |
|-------|-------|----------------|
| App Service (Admin) | `sradmin2` | `DefaultResourceGroup-PLC` |
| App Service Plan | `sportrental-free` | `DefaultResourceGroup-PLC` |
| Static Web App (Client) | `srclient-wasm` | `DefaultResourceGroup-PLC` |
| PostgreSQL | `eduedu.postgres.database.azure.com` | - |
| Database | `sr` | - |
| Key Vault | `vault2127` | `DefaultResourceGroup-PLC` |

---

## 7. Sekrety

Wszystkie sekrety są w folderze `secrets/` (gitignored):
- `secrets/azure-secrets.json` - pełna konfiguracja
- `secrets/README.md` - opis

**NIE COMMITUJ SEKRETÓW DO GITA!**



