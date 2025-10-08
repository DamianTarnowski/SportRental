# 🏢 Panel Administracyjny - Dane Firmy

## 📍 Lokalizacja

**URL:** `/admin/company-settings`

**Dostęp:** `Owner` lub `SuperAdmin`

---

## 🎨 Wygląd Panelu

### **Sekcja: Informacje podstawowe**

```
╔══════════════════════════════════════════════════════════╗
║ Informacje podstawowe                                    ║
╠══════════════════════════════════════════════════════════╣
║                                                           ║
║ ℹ️  Te dane będą automatycznie uwzględniane              ║
║     w umowach PDF wysyłanych do klientów!                ║
║                                                           ║
║ ┌─────────────────────────────────────────────────────┐ ║
║ │ Nazwa firmy *                                        │ ║
║ │ [Wypożyczalnia 'Narty & Snowboard' Sp. z o.o.]      │ ║
║ └─────────────────────────────────────────────────────┘ ║
║                                                           ║
║ ┌─────────────────────────────────────────────────────┐ ║
║ │ Adres                                                │ ║
║ │ [ul. Krupówki 12/3                            ]      │ ║
║ │ [34-500 Zakopane                              ]      │ ║
║ └─────────────────────────────────────────────────────┘ ║
║                                                           ║
║ ┌─────────────────────────────────────────────────────┐ ║
║ │ Telefon                                              │ ║
║ │ [+48 18 201 50 00]                                   │ ║
║ └─────────────────────────────────────────────────────┘ ║
║                                                           ║
║ ┌─────────────────────────────────────────────────────┐ ║
║ │ Email                                                │ ║
║ │ [kontakt@nartyzakopane.pl]                           │ ║
║ └─────────────────────────────────────────────────────┘ ║
║                                                           ║
║ ┌─────────────────────────────────────────────────────┐ ║
║ │ NIP                                                  │ ║
║ │ [7362614562]                      10 cyfr           │ ║
║ └─────────────────────────────────────────────────────┘ ║
║                                                           ║
║ ┌─────────────────────────────────────────────────────┐ ║
║ │ REGON                            ✨ NOWE!           │ ║
║ │ [012345678]                       9 lub 14 cyfr     │ ║
║ └─────────────────────────────────────────────────────┘ ║
║                                                           ║
║ ┌─────────────────────────────────────────────────────┐ ║
║ │ Forma prawna                                         │ ║
║ │ [Sp. z o.o.]                                         │ ║
║ └─────────────────────────────────────────────────────┘ ║
║                                                           ║
║ ┌─────────────────────────────────────────────────────┐ ║
║ │ Godziny otwarcia                                     │ ║
║ │ [Pn-Pt: 9:00-18:00, Sb-Nd: 10:00-16:00]            │ ║
║ └─────────────────────────────────────────────────────┘ ║
║                                                           ║
║ ┌─────────────────────────────────────────────────────┐ ║
║ │ Opis                                                 │ ║
║ │ [Profesjonalna wypożyczalnia sprzętu...      ]      │ ║
║ └─────────────────────────────────────────────────────┘ ║
║                                                           ║
╚══════════════════════════════════════════════════════════╝
```

---

## 🔧 Funkcjonalności

### ✅ **Pola formularza:**

| Pole | Typ | Wymagane | Opis |
|------|-----|----------|------|
| **Nazwa firmy** | Text | ✅ Tak | Pełna nazwa firmy (z Tenant) |
| **Adres** | Text (multi) | ❌ Nie | Pełny adres siedziby |
| **Telefon** | Text | ❌ Nie | Telefon kontaktowy |
| **Email** | Email | ❌ Nie | Email kontaktowy |
| **NIP** | Text (max 20) | ❌ Nie | 10 cyfr |
| **REGON** | Text (max 14) | ❌ Nie | 9 lub 14 cyfr |
| **Forma prawna** | Text | ❌ Nie | Np. "Sp. z o.o.", "JDG" |
| **Godziny otwarcia** | Text | ❌ Nie | Np. "Pn-Pt: 9-18" |
| **Opis** | Text (multi) | ❌ Nie | Krótki opis firmy |

### 📄 **Wpływ na umowy PDF:**

Po wypełnieniu tych danych, każda umowa PDF wysłana do klienta będzie zawierać:

```
╔════════════════════════════════════════════════╗
║ DANE WYPOŻYCZALNI                              ║
╠════════════════════════════════════════════════╣
║ Wypożyczalnia 'Narty & Snowboard' Sp. z o.o.  ║
║ Adres: ul. Krupówki 12/3, 34-500 Zakopane     ║
║ NIP: 7362614562                                ║
║ REGON: 012345678                               ║
║ Email: kontakt@nartyzakopane.pl                ║
║ Tel: +48 18 201 50 00                          ║
╚════════════════════════════════════════════════╝
```

**Footer PDF:**
```
Wypożyczalnia 'Narty & Snowboard' Sp. z o.o. | Wygenerowano: 07.10.2025
kontakt@nartyzakopane.pl | +48 18 201 50 00
```

---

## 💾 Zapisywanie

### **Logika zapisu:**

1. **Update Tenant:**
   - Aktualizacja `tenant.Name` (nazwa firmy)

2. **Update lub Create CompanyInfo:**
   - Sprawdza czy istnieje `CompanyInfo` dla danego `TenantId`
   - Jeśli istnieje → UPDATE wszystkich pól + `UpdatedAtUtc`
   - Jeśli nie → CREATE nowy rekord z wszystkimi polami

3. **Zapisywane pola w CompanyInfo:**
   - ✅ Address
   - ✅ PhoneNumber
   - ✅ Email
   - ✅ NIP
   - ✅ **REGON** (nowe!)
   - ✅ LegalForm
   - ✅ OpeningHours
   - ✅ Description
   - ✅ ExtraInfo
   - ✅ Lat, Lon (GPS)
   - ✅ SmsThanksText, SmsReminderText (x3)
   - ✅ EmailThanksText, EmailReminderText (x3)

4. **Potwierdzenie:**
   - Snackbar: "Ustawienia zostały zapisane" (Success)
   - Błąd: "Błąd podczas zapisywania: {message}" (Error)

---

## 🔗 Nawigacja

### **Dostęp z Panelu Owner:**

```
/admin/owner → Karta "Ustawienia firmy" → /admin/company-settings
```

### **Menu główne:**
```
Admin → Company Settings
```

---

## 🧪 Test Flow

### **Scenariusz testowy:**

1. **Login jako Owner**
   ```
   https://localhost:5016/login
   Role: Owner
   ```

2. **Przejdź do ustawień:**
   ```
   https://localhost:5016/admin/company-settings
   ```

3. **Wypełnij dane:**
   ```
   Nazwa firmy:   Wypożyczalnia 'Narty Test' Sp. z o.o.
   Adres:         ul. Testowa 1/2, 00-000 Warszawa
   Telefon:       +48 123 456 789
   Email:         test@wypo.pl
   NIP:           1234567890
   REGON:         123456789
   Forma prawna:  Sp. z o.o.
   ```

4. **Zapisz:**
   ```
   Kliknij "Zapisz ustawienia"
   Oczekiwany: "Ustawienia zostały zapisane" ✅
   ```

5. **Weryfikuj PDF:**
   - Przeprowadź wypożyczenie testowe
   - Sprawdź PDF w emailu
   - Upewnij się że dane firmy są w PDF! 📄

---

## 🎯 Użycie danych w systemie

### **Gdzie są wykorzystywane CompanyInfo:**

| Miejsce | Opis | Status |
|---------|------|--------|
| **PDF Contracts** | Dane firmy w sekcji "DANE WYPOŻYCZALNI" | ✅ Działa |
| **Email Footer** | Kontakt firmy w stopce emaili | ✅ Działa |
| **Stripe Webhooks** | Pobierane dla tenanta przy płatności | ✅ Działa |
| **SMS Templates** | Szablony z nazwą firmy | 🔄 TODO |
| **Faktury** | Dane firmy na fakturach | 🔄 TODO |

---

## 📊 Przykład działania

### **PRZED wypełnieniem:**
```
PDF Footer:
SportRental | kontakt@sportrental.pl | +48 123 456 789
```

### **PO wypełnieniu:**
```
PDF Header:
DANE WYPOŻYCZALNI
Wypożyczalnia 'Narty & Snowboard' Sp. z o.o.
Adres: ul. Krupówki 12/3, 34-500 Zakopane
NIP: 7362614562
REGON: 012345678
Email: kontakt@nartyzakopane.pl
Tel: +48 18 201 50 00

PDF Footer:
Wypożyczalnia 'Narty & Snowboard' Sp. z o.o. | 
Wygenerowano: 07.10.2025 00:14
kontakt@nartyzakopane.pl | +48 18 201 50 00
```

---

## 🚀 Deployment

### **Migracja bazy:**
```bash
# REGON już dodany w migracji
dotnet ef database update --project SportRental.Infrastructure --startup-project SportRental.Admin
```

### **Build i Run:**
```bash
# Build
dotnet build SportRental.Admin/SportRental.Admin.csproj

# Run
dotnet run --project SportRental.Admin
```

### **URL:**
```
https://localhost:5016/admin/company-settings
```

---

## 📋 Checklist wdrożenia

- [x] Pole REGON dodane do modelu CompanyInfo
- [x] Migracja EF stworzona i uruchomiona
- [x] UI panel ma pole REGON
- [x] LoadData mapuje REGON
- [x] Save zapisuje REGON (update + create)
- [x] CompanyInfoDto ma REGON
- [x] Info box o PDF w UI
- [x] Build SportRental.Admin OK
- [x] PDF generator używa CompanyInfo
- [x] Testy PDF przechodzą

---

## 💡 Tips dla Owner

### **Najlepsze praktyki:**

1. **Wypełnij wszystkie pola!**
   - Im więcej danych, tym profesjonalniejsze umowy PDF

2. **Sprawdź NIP i REGON:**
   - Upewnij się że są poprawne
   - NIP: 10 cyfr
   - REGON: 9 lub 14 cyfr

3. **Używaj pełnego adresu:**
   - Ulica, numer, kod pocztowy, miasto
   - To będzie w umowach dla klientów!

4. **Email i telefon:**
   - To główne kanały kontaktu w umowach
   - Sprawdź czy są aktualne

5. **Przetestuj PDF:**
   - Po wypełnieniu zrób testowe wypożyczenie
   - Sprawdź czy PDF wygląda OK

---

## 🆘 Troubleshooting

### **Problem: Dane nie zapisują się**
```
Rozwiązanie:
1. Sprawdź logi w konsoli przeglądarki
2. Sprawdź czy jesteś zalogowany jako Owner
3. Sprawdź connection string do bazy
```

### **Problem: PDF nie ma moich danych**
```
Rozwiązanie:
1. Sprawdź czy CompanyInfo istnieje w bazie
2. SELECT * FROM "CompanyInfos" WHERE "TenantId" = 'your-tenant-id'
3. Sprawdź czy webhook pobiera CompanyInfo
```

### **Problem: REGON nie zapisuje się**
```
Rozwiązanie:
1. Sprawdź migrację: dotnet ef migrations list
2. Upewnij się że AddREGONToCompanyInfo została zastosowana
3. Sprawdź czy kolumna REGON istnieje w bazie
```

---

**Data:** 07.10.2025  
**Wersja:** 1.0  
**Status:** ✅ Production Ready  
**Autor:** AI Assistant  
