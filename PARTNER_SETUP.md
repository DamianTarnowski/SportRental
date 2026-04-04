# 🛠️ Instrukcja konfiguracji lokalnej dla Wspólnika

Witaj w projekcie SportRental! Poniżej znajdziesz kroki niezbędne do uruchomienia całego systemu na Twoim komputerze bez konieczności posiadania dostępu do Azure.

## 📋 Wymagania wstępne (Prerequisites)

Zainstaluj następujące narzędzia:
1. **Git**
2. **.NET 10 SDK**
3. **Docker Desktop** (do bazy danych PostgreSQL)
4. **PowerShell 7+** (rekomendowany)

---

## 🚀 Kroki instalacji

### 1. Pobranie repozytorium
```bash
git clone https://github.com/DamianTarnowski/SportRental.git
cd SportRental
```

### 2. Konfiguracja ustawień (Ważne!)
Otrzymasz ode mnie (lub od Damiana) plik z sekretami. Musisz go zapisać w dwóch miejscach:
- `SportRental.Admin/appsettings.Development.json`
- `SportRental.Api/appsettings.Development.json`

Te pliki są ignorowane przez Git, więc Twoje klucze pozostaną bezpieczne na Twoim dysku.

### 3. Uruchomienie bazy danych (Docker)
W głównym folderze projektu uruchom kontener z PostgreSQL:
```bash
docker-compose up -d
```

### 4. Inicjalizacja bazy i danych testowych
Uruchom skrypt, który utworzy strukturę bazy i załaduje dane testowe (sprzęt, klienci, konta):
```powershell
.\reseed-database.ps1
```

---

## 💻 Uruchamianie projektu

Najprostszy sposób na codzienną pracę:
```powershell
.\start-dev-simple.ps1
```

---

## 🔐 Dane do logowania (Testowe)

- **Login:** `admin@sportrental.pl`
- **Hasło:** `Admin123!`

W razie pytań uderzaj śmiało! 🚀
