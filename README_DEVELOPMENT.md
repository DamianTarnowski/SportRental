# 🚀 SportRental - Development Guide

## Quick Start

### 🎯 **Option 1: PowerShell Script (Recommended)**

Najprostszy sposób - uruchamia Admin API + WASM Client w dwóch terminalach:

```powershell
.\start-dev-simple.ps1
```

Co się dzieje:
- 🔌 Terminal 1: Admin API (backend + TestDataSeeder)
- 📱 Terminal 2: WASM Client (frontend)
- 🌐 Automatycznie otwiera przeglądarkę na http://localhost:5014

**Aby zatrzymać:** Zamknij oba terminale.

---

### 🎯 **Option 2: VS Code / Cursor**

1. Otwórz panel **Run and Debug** (Ctrl+Shift+D)
2. Wybierz: **🎯 Full Stack Development**
3. Kliknij **Start Debugging** (F5)

Co się dzieje:
- ✅ Build Admin + Client
- ✅ Uruchamia Admin API
- ✅ Uruchamia WASM Client
- ✅ Otwiera debugger

**Aby zatrzymać:** Kliknij **Stop** (Shift+F5) - zatrzyma oba procesy.

---

### 🎯 **Option 3: Visual Studio / Rider**

1. Otwórz `SportRental.sln`
2. Kliknij prawym na solution → **Properties**
3. **Multiple Startup Projects**
4. Ustaw:
   - `SportRental.Admin` → **Start**
   - `SportRental.Client` → **Start**
5. Kliknij **OK**
6. Naciśnij **F5** (Start Debugging)

**Aby zatrzymać:** Kliknij **Stop** - zatrzyma oba projekty.

---

### 🎯 **Option 4: Manual (Old School)**

**Terminal 1 (Backend):**
```bash
cd SportRental.Admin
dotnet run
```

**Terminal 2 (Frontend):**
```bash
cd SportRental.Client
dotnet run
```

**Przeglądarka:**
```
http://localhost:5014
```

---

## 🌐 URLs

Po uruchomieniu dostępne są:

| Service | URL | Description |
|---------|-----|-------------|
| 📱 **WASM Client** | http://localhost:5014 | Frontend (public) |
| 🔌 **Admin API** | http://localhost:5001 | Backend API |
| 🎛️ **Admin Panel** | http://localhost:5001 | Blazor Server UI |
| 🗄️ **PostgreSQL** | localhost:5432 | Database |

---

## 📦 Test Data

Admin API automatycznie ładuje dane z `test-data.json` przy pierwszym uruchomieniu:

- 🏢 **3 Tenants** (wypożyczalnie)
- 🎿 **16 Products** (narty, rowery, SUP...)
- 👥 **5 Customers**
- 🔐 **2 Test Accounts** (konta do logowania)

### Test Cards (Stripe Sandbox):
- ✅ `4242 4242 4242 4242` - Success
- ❌ `4000 0000 0000 0002` - Declined
- 🔐 `4000 0025 0000 3155` - 3D Secure

---

## 🔧 Development Workflows

### Hot Reload (Auto-refresh on code changes)

**Admin API:**
```bash
cd SportRental.Admin
dotnet watch run
```

**WASM Client:**
```bash
cd SportRental.Client
dotnet watch run
```

### Run Tests

```bash
# All tests
dotnet test

# Specific project
dotnet test SportRental.Api.Tests

# With coverage
dotnet test /p:CollectCoverage=true
```

---

## 🐛 Troubleshooting

### "No products" w WASM Client

**Przyczyna:** Brak uruchomionego backendu (Admin API)

**Rozwiązanie:**
```bash
cd SportRental.Admin
dotnet run
```

### Port już zajęty

**Przyczyna:** Inny proces używa portu 5001 lub 5014

**Rozwiązanie:**
```powershell
# Windows
netstat -ano | findstr ":5001"
taskkill /PID <PID> /F

# Linux/Mac
lsof -ti:5001 | xargs kill
```

### Database connection error

**Przyczyna:** PostgreSQL nie działa

**Rozwiązanie:**
```bash
# Check status
docker ps | grep postgres

# Start PostgreSQL
docker start <container_id>
```

---

## 🔐 Environment Variables

Dla Development, zmienne są w:
- `SportRental.Admin/appsettings.Development.json`
- `SportRental.Api/appsettings.Development.json`

**NIGDY** nie commituj tych plików (są w `.gitignore`).

Dla produkcji użyj **Azure Key Vault** (jak skonfigurowane w `Program.cs`).

---

## 📚 Architecture

```
SportRental/
├── SportRental.Admin/          # Blazor Server (Admin Panel + API)
├── SportRental.Api/            # Minimal APIs (Public API)
├── SportRental.Client/         # Blazor WASM (Public Client)
├── SportRental.Infrastructure/ # EF Core, Domain models
├── SportRental.Shared/         # Shared DTOs, Services
├── SportRental.MediaStorage/   # Media microservice
└── SportRental.*.Tests/        # Test projects
```

**Data Flow:**
```
WASM Client (Browser)
    ↓ HTTP
Admin API (Backend)
    ↓ EF Core
PostgreSQL (Database)
```

---

## 🚀 Next Steps

1. ✅ Run `.\start-dev-simple.ps1`
2. ✅ Open http://localhost:5014
3. ✅ Browse products
4. ✅ Add to cart
5. ✅ Checkout with test card `4242 4242 4242 4242`
6. ✅ See confirmation!

---

## 📧 Questions?

Contact: hdtdtr@gmail.com

---

**Happy Coding! 🎉**






















