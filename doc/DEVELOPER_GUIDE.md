# SportRental Developer Guide

> Looking for a portfolio pitch? Check `docs/SHOWCASE.md` for a curated overview and demo script.

> **Ostatnia aktualizacja:** Grudzień 2025

## ⚠️ Aktualna architektura

**WAŻNE:** Obecna architektura jest uproszczona:

- **SportRental.Admin** - Blazor Server hostujący panel administracyjny **ORAZ API dla klienta WASM**
- **SportRental.Client** - Blazor WASM łączący się z API w Admin
- **SportRental.Api** - ⏸️ **WYŁĄCZONY** - przygotowany na przyszłość
- **SportRental.MediaStorage** - ⏸️ **WYŁĄCZONY** - pliki w Azure Blob Storage

## Wymagania wstepne
- Windows/Linux/macOS z aktualnym PowerShell lub bash.
- .NET 10 SDK oraz narzedzie Entity Framework CLI (`dotnet tool install --global dotnet-ef`).
- **Azure CLI** (`az`) - WYMAGANE dla lokalnego developmentu! https://aka.ms/installazurecliwindows
- **Dostęp do Azure Key Vault** - sekrety są przechowywane TYLKO w Key Vault (nie w plikach!).
- PostgreSQL 15+ (lokalny lub kontener Azure). Connection string w Key Vault.
- Node.js 18+ oraz npm (wymagane do Tailwind w projekcie klienta WASM).
- Opcjonalnie: przegladarka Edge/Chrome, Visual Studio 2022 17.10+, Rider, VS Code.

## ⚠️ WAŻNE: Przed rozpoczęciem!

**Projekt używa Azure Key Vault dla WSZYSTKICH sekretów!**

1. **Przeczytaj:** [SECURITY.md](../SECURITY.md)
2. **Skonfiguruj:** [doc/setup/AZURE_KEY_VAULT_SETUP.md](setup/AZURE_KEY_VAULT_SETUP.md)
3. **Zaloguj się:** `az login --tenant YOUR-TENANT-ID`
4. **Poproś o dostęp** do Key Vault (jeśli nie masz)

## Pierwsze kroki
```bash
git clone <repo-url>
cd SportRentalHybrid
dotnet restore SportRentalHybrid.sln
```

## Konfiguracja klienta WASM
- Edytuj `SportRental.Client/wwwroot/appsettings.json`, aby ustawic `Api:BaseUrl` i `Api:TenantId`.
- Opcjonalnie dodaj pliki `appsettings.{Environment}.json` w tym katalogu dla innych konfiguracji.
- Gdy konfiguracja jest pusta, klient uzywa adresu hosta i nie wysyla `X-Tenant-Id`.
- Dane profilu klienta sďż˝ utrzymywane w localStorage (`CustomerSessionService`). Wyczyszczenie sesji (np. w dev tools) powoduje wymaganie ponownego logowania.
## Konfiguracja bazy danych

**UWAGA:** Connection string jest w Azure Key Vault, nie w plikach!

1. Baza produkcyjna jest na Azure PostgreSQL (connection string w Key Vault).
2. Jeśli musisz zmienić connection string:
   ```bash
   # Aktualizuj sekret w Key Vault
   az keyvault secret set \
     --vault-name YOUR-VAULT-NAME \
     --name "ConnectionStrings--DefaultConnection" \
     --value "Host=YOUR-HOST;Port=5432;Database=sr;Username=...;Password=...;SSL Mode=Require"
   ```
3. Connection string używany jest przez:
   - `SportRental.Admin` (panel administracyjny + API)
   - Pobiera automatycznie z Key Vault przy starcie.

## Migracje EF Core
- Zastosowanie migracji:
  ```bash
  dotnet ef database update --project SportRental.Admin
  ```
- Dodanie nowej migracji:
  ```bash
  dotnet ef migrations add <NazwaMigracji> --project SportRental.Admin --startup-project SportRental.Admin
  ```
- Czyszczenie migracji (tylko gdy naprawde potrzebne):
  ```bash
  dotnet ef migrations remove --project SportRental.Admin
  ```

## Uruchamianie uslug

**NAJPIERW: Zaloguj się do Azure!**
```bash
# WYMAGANE przed uruchomieniem aplikacji!
az login --tenant YOUR-TENANT-ID

# Sprawdź czy zalogowano:
az account show
```

### Opcja A: Visual Studio (ZALECANE)
1. Otwórz `SportRentalHybrid.sln`
2. Przy przycisku Start kliknij strzałkę w dół
3. Wybierz profil **"Admin + Client"**
4. Naciśnij F5 - uruchomią się oba projekty

### Opcja B: Terminal
```bash
# 1. Panel administracyjny + API (pobiera sekrety z Key Vault)
dotnet run --project SportRental.Admin --urls "http://localhost:5001"
# Musisz zobaczyć: 🔐 Azure Key Vault configured: https://...

# 2. Klient WASM (w osobnym terminalu)
dotnet run --project SportRental.Client --urls "http://localhost:5014"
```

> **UWAGA:** Projekty `SportRental.Api` i `SportRental.MediaStorage` są obecnie **WYŁĄCZONE**.
> API jest hostowane w `SportRental.Admin`, pliki w Azure Blob Storage.

**Jeśli NIE widzisz "🔐 Azure Key Vault configured":**
1. Sprawdź `appsettings.Development.json` → `KeyVault:Url` musi być ustawione
2. Sprawdź `az account show` → musisz być zalogowany
3. Sprawdź permissions: `az keyvault secret list --vault-name YOUR-VAULT-NAME`

## Role i konta Identity
- Wspolne stale rĂłl znajduja sie w `SportRental.Shared.Identity.RoleNames` (`SuperAdmin`, `Owner`, `Employee`, `Client`).
- Przy starcie `SportRental.Admin` seeduje wszystkie role oraz konto konfiguracyjne z `Admin:Email`/`Admin:Password`, dodajac mu rowniez role `Client`.
- Strona `/setup` promuje zalogowanego uzytkownika na wlasciciela (`Owner`) i gwarantuje role `Client`, oraz wpis do tabeli `TenantUsers`.
- Endpoint `POST /api/employees` (panel) tworzy lub aktualizuje konto Identity pracownika, przypisuje role `Employee` + `Client` oraz zwraca w odpowiedzi tymczasowe haslo przy pierwszym utworzeniu (do przekazania uzytkownikowi).
- Usuniecie pracownika z panelu usuwa przypisanie roli `Employee`, ale pozostawia `Client`, aby nadal mogl korzystac z frontu klienckiego.

## Tailwind i zasoby statyczne (SportRental.Client)
```bash
cd SportRental.Client
npm install
npm run watch:css   # generowanie css/tailwind.config
```
Do buildow CI wystarczy `npm run build:css` (dodaj zgodnie z potrzebami).

## Testy
- Wszystkie testy:
  ```bash
  dotnet test SportRentalHybrid.sln
  ```
- Panel administracyjny:
  ```bash
  dotnet test SportRental.Admin.Tests/SportRental.Admin.Tests.csproj --no-build
  ```
- Mikroserwis plikow:
  ```bash
  dotnet test SportRental.MediaStorage.Tests/SportRental.MediaStorage.Tests.csproj --no-build
  ```
- Klient WASM:
  ```bash
  dotnet test SportRental.Client.Tests/SportRental.Client.Tests.csproj --no-build
  ```
- Pokrycie kodu:
  ```bash
  dotnet test SportRental.Admin.Tests/SportRental.Admin.Tests.csproj --collect:"XPlat Code Coverage"
  ```
- Uruchamianie konkretnej kategorii:
  ```bash
  dotnet test SportRental.Admin.Tests/SportRental.Admin.Tests.csproj --filter Category=Api
  ```

## Styl kodu i narzedzia
- Formatowanie: `dotnet format` (respektuje `.editorconfig`).
- Analizatory: projekt korzysta z wbudowanych analizatorow Roslyn (WarningsAsErrors dla kluczowych projektow).
- Konwencje:
  - Klasy i pliki: PascalCase (`RentalSummaryComponent.razor`).
  - Prywatne pola: `_camelCase`.
  - Metody asynchroniczne: sufiks `Async`.
  - Minimalne komentarze, preferuj czytelny kod.

## Debugowanie i hot reload
- Panel/Client: `dotnet watch --project SportRental.Admin` lub obsuga Visual Studio (`F5`).
- `SportRental.Api` mozna uruchomic z `ASPNETCORE_ENVIRONMENT=Development`, co wlacza Swagger i rozszerzone logowanie.
- Do sledzenia zapytan EF Core wlacz logowanie poziomu `Information` w `appsettings.Development.json` (`Logging:LogLevel:Microsoft.EntityFrameworkCore=Information`).

## Czeste problemy
| Problem | Rozwiazanie |
|---|---|
| Brak dostepu do bazy | Sprawdz `ConnectionStrings:DefaultConnection`, upewnij sie ze PostgreSQL nasluchuje na 5432 i certyfikat SSL nie blokuje polaczenia. |
| Port zajety przez inny proces | Zmien port w `launchSettings.json` lub uzyj `dotnet run --urls`. |
| MediaStorage zwraca 401 | Dodaj poprawny `X-Api-Key` (konfiguracja `SecurityOptions:ApiKeys`). |
| Bledy TLS przy WASM | Uruchom `dotnet dev-certs https --trust` i zrestartuj przegladarke. |
| Testy API koncza sie NullReference w cleanup | Upewnij sie, ze korzystasz z aktualnego `MediaStorageProcessHostedService` (wersja z `Interlocked.Exchange`). |

## Przydatne komendy
```bash
# czyszczenie build artifacts
dotnet clean SportRentalHybrid.sln
find . -name bin -o -name obj | ForEach-Object { Remove-Item $_ -Recurse -Force }

# analiza migracji
dotnet ef migrations list --project SportRental.Admin

# eksport schematu (PostgreSQL)
dotnet ef dbcontext script --project SportRental.Admin
```

## 📱 Responsywne UI (Mobile-First)

### Dual UI Strategy
Projekt wykorzystuje **osobne widoki** dla mobile (<768px) i desktop. W komponentach Razor:

```razor
@if (_isMobile)
{
    <!-- Mobile UI -->
}
else
{
    <!-- Desktop UI -->
}
```

### Mobile Detection (JS Interop)
Plik `wwwroot/js/mobile-detection.js` wykrywa szerokość ekranu:

```csharp
// W komponencie Blazor:
@inject IJSRuntime JS
@implements IAsyncDisposable

private bool _isMobile;
private DotNetObjectReference<MyComponent>? _dotNetRef;

protected override async Task OnAfterRenderAsync(bool firstRender)
{
    if (firstRender)
    {
        _dotNetRef = DotNetObjectReference.Create(this);
        await JS.InvokeVoidAsync("setupMobileDetection", _dotNetRef);
    }
}

[JSInvokable]
public void OnScreenResize(bool isMobile)
{
    _isMobile = isMobile;
    StateHasChanged();
}

public async ValueTask DisposeAsync()
{
    await JS.InvokeVoidAsync("removeMobileDetection");
    _dotNetRef?.Dispose();
}
```

### Testowanie responsywności
1. **Chrome DevTools** - F12 → Toggle device toolbar (Ctrl+Shift+M)
2. **Breakpoint:** 768px (mobile < 768px, desktop >= 768px)
3. **Sprawdź:** sticky headers, bottom summaries, slidable panels

### Dark Mode (Admin)
- `ThemeSwitcher.razor` - przełącznik motywu
- Persystencja w localStorage
- CSS variables w `admin-theme.css`

### Mapa Leaflet
- **Dodanie do komponentu:**
  ```razor
  <LeafletMap Latitude="52.2297" Longitude="21.0122" Zoom="13" />
  ```
- **JS Interop:** `leaflet-interop.js` (Admin), `leaflet-map.js` (Client)

## Dalsza lektura
- Dokumentacja Blazor: https://learn.microsoft.com/aspnet/core/blazor/
- Entity Framework Core: https://learn.microsoft.com/ef/core/
- MudBlazor: https://mudblazor.com/
- Minimal APIs: https://learn.microsoft.com/aspnet/core/fundamentals/minimal-apis
- Leaflet.js: https://leafletjs.com/

Powodzenia! W razie watpliwosci sprawdz pozostale pliki dokumentacji lub otworz issue w repozytorium.




