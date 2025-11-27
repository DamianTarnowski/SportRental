# 🎯 SportRental Database Tool

**Bezpieczne, standalone narzędzie do przeglądania bazy danych PostgreSQL.**

---

## 🚀 Jak uruchomić?

### Opcja 1: Helper skrypt (zalecane)
```powershell
.\db-tool.ps1
```

### Opcja 2: Bezpośrednio
```powershell
cd SportRental.DbTool
dotnet run
```

---

## ✨ Funkcje

### 1️⃣ **Wybór bazy danych**
- `sr_test` - baza testowa (Development)
- `sr` - baza produkcyjna

### 2️⃣ **Lista tabel**
Wyświetla wszystkie tabele w bazie wraz z liczbą kolumn.

### 3️⃣ **Wykonywanie SQL queries**
- ✅ Tylko **SELECT** queries (read-only)
- ✅ Limit 100 wierszy dla bezpieczeństwa
- ✅ Ładne formatowanie w tabeli
- ✅ Automatyczna historia zapytań

### 4️⃣ **Szybkie statystyki**
Pokazuje liczby rekordów w głównych tabelach:
- Produkty
- Tenanci
- Klienci
- Wynajmy
- Aktywne Holds

### 5️⃣ **Historia zapytań**
Zapisuje ostatnie 20 wykonanych queries.

### 6️⃣ **Eksport do CSV**
Eksportuje wyniki ostatniego query do pliku CSV z timestampem.

---

## 📊 Przykładowe queries

### Wszystkie produkty
```sql
SELECT "Name", "Category", "DailyPrice", "AvailableQuantity" 
FROM "Products" 
ORDER BY "DailyPrice" DESC
```

### Produkty w kategorii
```sql
SELECT "Name", "DailyPrice" 
FROM "Products" 
WHERE "Category" = 'Narty'
```

### Statystyki produktów po kategoriach
```sql
SELECT 
  "Category", 
  COUNT(*) as total,
  SUM("AvailableQuantity") as available,
  AVG("DailyPrice")::numeric(10,2) as avg_price
FROM "Products" 
GROUP BY "Category"
ORDER BY total DESC
```

### Wszyscy tenanci
```sql
SELECT "Id", "Name", "CreatedAtUtc" 
FROM "Tenants" 
ORDER BY "Name"
```

### Klienci z emailami
```sql
SELECT "FullName", "Email", "PhoneNumber" 
FROM "Customers" 
WHERE "Email" IS NOT NULL
ORDER BY "FullName"
```

### Aktywne wynajmy
```sql
SELECT 
  r."Id",
  c."FullName" as customer,
  r."StartDateUtc",
  r."EndDateUtc",
  r."Status"
FROM "Rentals" r
JOIN "Customers" c ON r."CustomerId" = c."Id"
WHERE r."Status" IN (1, 2, 3)
ORDER BY r."StartDateUtc" DESC
```

### Struktura tabeli
```sql
SELECT column_name, data_type, is_nullable, column_default
FROM information_schema.columns
WHERE table_name = 'Products'
ORDER BY ordinal_position
```

---

## 🔒 Bezpieczeństwo

- ✅ **Standalone** - nie jest częścią aplikacji produkcyjnej
- ✅ **Read-only** - tylko SELECT queries
- ✅ **Lokalne** - działa tylko na Twoim komputerze
- ✅ **Connection strings** - hardcoded w aplikacji (tylko dla Ciebie)
- ✅ **Limit 100 wierszy** - zabezpieczenie przed dużymi wynikami

---

## 💡 Wskazówki

### PostgreSQL cudzysłowy
W PostgreSQL nazwy tabel/kolumn wymagają **podwójnych cudzysłowów**:
```sql
SELECT "Name" FROM "Products"  -- ✅ Dobrze
SELECT Name FROM Products      -- ❌ Błąd (jeśli nazwa ma wielkie litery)
```

### Apostrofy w wartościach
Dla tekstów używaj **pojedynczych apostrofów**:
```sql
WHERE "Category" = 'Narty'     -- ✅ Dobrze
WHERE "Category" = "Narty"     -- ❌ Błąd
```

### Escape apostrofów
Jeśli wartość zawiera apostrof, użyj podwójnego:
```sql
WHERE "Name" = 'Rower ''Premium'''  -- O'Reilly → O''Reilly
```

---

## 📦 Zależności

- **.NET 9.0**
- **Npgsql 9.0.2** - PostgreSQL driver
- **Spectre.Console 0.49.1** - ładne UI w konsoli

---

## 🎯 Zalety

1. ✅ **Bezpieczne** - nie wpływa na aplikację produkcyjną
2. ✅ **Wygodne** - interaktywne menu, ładne tabele
3. ✅ **Funkcjonalne** - wszystko czego potrzebujesz do przeglądania bazy
4. ✅ **Szybkie** - bezpośrednie połączenie z PostgreSQL
5. ✅ **Historia** - pamięta Twoje zapytania
6. ✅ **Eksport** - zapisz wyniki do CSV

---

## 🐛 Rozwiązywanie problemów

### Błąd połączenia
Upewnij się że:
- Masz dostęp do Internetu (baza na Azure)
- Hasło w `Program.cs` jest poprawne (4x `@`)

### Timeout
Jeśli query trwa za długo:
- Dodaj `LIMIT` do swojego query
- Zwiększ `cmd.CommandTimeout` w kodzie

### Błąd składni SQL
Sprawdź:
- Czy używasz podwójnych cudzysłowów dla nazw: `"Products"`
- Czy używasz pojedynczych apostrofów dla wartości: `'Narty'`

---

**Enjoy! 🚀**



