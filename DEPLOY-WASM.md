# Deploy WASM Client na Azure App Service

## Wymagania
- Azure CLI zalogowany (`az login`)
- .NET SDK 10.0+

## Komendy Deploy

### 1. Publish i deploy klienta WASM
```powershell
# Usuń stary publish
Remove-Item "./publish/client" -Recurse -Force -ErrorAction SilentlyContinue

# Publish
dotnet publish SportRental.Client -c Release -o ./publish/client

# WAŻNE: W .NET 10 pliki WASM są w _client/
Compress-Archive -Path "./publish/client/wwwroot/_client/*" -DestinationPath "./publish/client-wasm.zip" -Force

# Deploy na Azure (--clean true wymusza nadpisanie wszystkich plików)
az webapp deploy --resource-group DefaultResourceGroup-PLC --name srclient-blazor --src-path ./publish/client-wasm.zip --type zip --clean true
```

### 2. Jedna komenda (wszystko razem)
```powershell
Remove-Item "./publish/client" -Recurse -Force -ErrorAction SilentlyContinue; dotnet publish SportRental.Client -c Release -o ./publish/client; Compress-Archive -Path "./publish/client/wwwroot/_client/*" -DestinationPath "./publish/client-wasm.zip" -Force; az webapp deploy --resource-group DefaultResourceGroup-PLC --name srclient-blazor --src-path ./publish/client-wasm.zip --type zip --clean true
```

## URL
- **Client WASM**: https://srclient-blazor.azurewebsites.net
- **Admin API**: https://sradmin.azurewebsites.net

## Ważne uwagi

### Struktura plików w .NET 10
W .NET 10 Blazor WASM publikuje pliki do `wwwroot/_client/` zamiast bezpośrednio do `wwwroot/`. 
**Zawsze używaj `_client/*` przy tworzeniu ZIP!**

### web.config
Plik `SportRental.Client/wwwroot/web.config` musi zawierać:
1. Reguły SPA rewrite (przekierowanie do index.html)
2. **Wykluczenie** plików `_framework/*`, `.wasm`, `.dll`, `.json` z rewrite
3. MIME types dla WASM

### Troubleshooting

#### Błąd "expected magic word 00 61 73 6d"
Pliki `.wasm` są przekierowywane do `index.html`. Sprawdź reguły rewrite w web.config.

#### Stare pliki na serwerze
Użyj `--clean true` przy deploy lub wyczyść wwwroot przez Kudu:
```powershell
$creds = az webapp deployment list-publishing-credentials --resource-group DefaultResourceGroup-PLC --name srclient-blazor --query "{user:publishingUserName,pass:publishingPassword}" -o json | ConvertFrom-Json
$pair = "$($creds.user):$($creds.pass)"
$bytes = [System.Text.Encoding]::ASCII.GetBytes($pair)
$base64 = [System.Convert]::ToBase64String($bytes)
$headers = @{Authorization = "Basic $base64"}
Invoke-RestMethod -Uri "https://srclient-blazor.scm.azurewebsites.net/api/vfs/site/wwwroot/?recursive=true" -Method DELETE -Headers $headers
```

#### 0 produktów / błędy API
1. Sprawdź czy `ApiService` używa pełnych URL (`$"{_baseUrl}/api/...`)
2. Sprawdź `PropertyNameCaseInsensitive = true` w JsonSerializerOptions
3. Sprawdź CORS w Admin API

## Konfiguracja

### Client appsettings.Production.json
```json
{
  "ApiBaseUrl": "https://sradmin.azurewebsites.net"
}
```

### Admin CORS (Program.cs)
```csharp
policy.WithOrigins(
    "https://srclient-blazor.azurewebsites.net",
    // ... inne URL
)
```
