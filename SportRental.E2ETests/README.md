# 🎭 SportRental E2E Tests (Playwright)

## 📋 Podsumowanie

**Automatyczne testy End-to-End** dla aplikacji klienckiej SportRental wykorzystujące **Playwright**.

### ✅ **Wyniki testów:**
- ✅ **27 testów przeszło pomyślnie**
- ⏭️ **6 testów pominięto** (wymagają danych testowych)
- ❌ **0 testów nie powiodło się**
- ⏱️ **Czas wykonania:** ~1 minuta 44 sekundy

---

## 🧪 **Zakres testów**

### **1. Strona Główna** (5 testów) ✅
- ✅ Ładowanie strony i wyświetlanie Hero section
- ✅ Nawigacja do katalogu produktów
- ✅ Wyświetlanie sekcji Features
- ✅ Widoczność ikony koszyka
- ✅ Wszystkie linki w menu

### **2. Katalog Produktów** (7 testów) ✅
- ✅ Ładowanie katalogu
- ✅ Pole wyszukiwania
- ✅ Filtry (kategoria, sortowanie, dostępność)
- ✅ Karty produktów z przyciskiem "Dodaj do koszyka"
- ✅ Przycisk "Zobacz szczegóły"
- ✅ Paginacja
- ✅ Statystyki (liczba produktów, dostępne, średnia cena)

### **3. Szczegóły Produktu** (6 testów) ⏭️
- ⏭️ Ładowanie strony szczegółów (wymaga produktów w bazie)
- ⏭️ Date pickery (data start/end)
- ⏭️ Przycisk "Dodaj do koszyka"
- ⏭️ Lightbox ze zdjęciem
- ⏭️ Sekcja "Powiązane produkty"

### **4. Koszyk** (6 testów) ✅
- ✅ Pusty koszyk - komunikat
- ✅ Koszyk z produktami
- ✅ Przyciski +/- (zmiana ilości)
- ✅ Edycja dat wynajmu
- ✅ Przycisk "Przejdź do płatności"
- ✅ Podsumowanie zamówienia

### **5. Checkout** (5 testów) ✅
- ✅ Pusty koszyk - ostrzeżenie
- ✅ Formularz danych klienta
- ✅ Podsumowanie płatności
- ✅ Przycisk potwierdzenia
- ✅ Wyszukiwanie klienta po emailu

### **6. Kontakt** (5 testów) ✅
- ✅ Ładowanie strony
- ✅ Dane kontaktowe (telefon, email, adres)
- ✅ Formularz kontaktowy
- ✅ Przycisk wysyłania
- ✅ Linki social media

---

## 🚀 **Jak uruchomić testy**

### **Wymagania:**
1. **.NET 9+**
2. **Uruchomiona aplikacja:**
   - Backend API: `http://localhost:5242`
   - Frontend Client: `http://localhost:5014`

### **Krok 1: Uruchom aplikację**

W głównym katalogu projektu:

```powershell
# Opcja A: Użyj skryptu startowego
.\start-dev.ps1

# Opcja B: Ręcznie (2 osobne terminale)
# Terminal 1 - API
cd SportRental.Api
dotnet run

# Terminal 2 - Client
cd SportRental.Client
dotnet run
```

### **Krok 2: Uruchom testy**

```powershell
cd SportRental.E2ETests/SportRental.E2ETests

# Wszystkie testy
dotnet test

# Testy z szczegółowymi logami
dotnet test --logger:"console;verbosity=detailed"

# Tylko konkretna grupa testów
dotnet test --filter "FullyQualifiedName~HomePage"
dotnet test --filter "FullyQualifiedName~ProductCatalog"
dotnet test --filter "FullyQualifiedName~Cart"
dotnet test --filter "FullyQualifiedName~Checkout"
dotnet test --filter "FullyQualifiedName~Contact"
```

### **Krok 3: Oglądaj przeglądarką**

Testy uruchamiają się z **widoczną przeglądarką** (Headless=false) i **zwolnionym tempem** (SlowMo=100ms), więc możesz oglądać co się dzieje!

---

## 📸 **Screenshoty**

Każdy test robi screenshot strony. Screenshoty zapisywane są w katalogu `screenshots/` z timestampem.

Przykładowe screenshoty:
- `01_home_page_*.png` - Strona główna
- `06_product_catalog_*.png` - Katalog produktów
- `18_empty_cart_*.png` - Pusty koszyk
- `29_contact_page_*.png` - Strona kontaktu

---

## ⚙️ **Konfiguracja**

Ustawienia znajdują się w pliku `playwright.runsettings`:

```xml
<Playwright>
  <BrowserName>chromium</BrowserName>
  <LaunchOptions>
    <Headless>false</Headless>      <!-- Widoczna przeglądarka -->
    <SlowMo>100</SlowMo>             <!-- Zwolnienie o 100ms -->
  </LaunchOptions>
  <ExpectTimeout>5000</ExpectTimeout>  <!-- Timeout dla asercji -->
  <Timeout>30000</Timeout>             <!-- Timeout dla akcji -->
</Playwright>
```

### **Zmiana przeglądarki:**

```xml
<BrowserName>chromium</BrowserName>  <!-- Chrome/Edge -->
<BrowserName>firefox</BrowserName>   <!-- Firefox -->
<BrowserName>webkit</BrowserName>    <!-- Safari -->
```

### **Tryb headless (bez okna):**

```xml
<Headless>true</Headless>
```

---

## 📊 **Struktura testów**

```
SportRental.E2ETests/
├── BaseTest.cs                      # Bazowa klasa testowa
├── HomePageTests.cs                 # Testy strony głównej
├── ProductCatalogTests.cs           # Testy katalogu
├── ProductDetailsTests.cs           # Testy szczegółów produktu
├── CartTests.cs                     # Testy koszyka
├── CheckoutTests.cs                 # Testy checkout
├── ContactTests.cs                  # Testy strony kontaktu
├── playwright.runsettings           # Konfiguracja Playwright
└── screenshots/                     # Katalog na screenshoty
```

---

## 🐛 **Rozwiązywanie problemów**

### **Problem:** Testy nie mogą połączyć się z aplikacją

**Rozwiązanie:**
1. Sprawdź czy aplikacja działa:
   ```powershell
   Test-NetConnection -ComputerName localhost -Port 5014
   Test-NetConnection -ComputerName localhost -Port 5242
   ```
2. Uruchom aplikację przed testami
3. Sprawdź czy porty się zgadzają w `BaseTest.cs`

### **Problem:** Niektóre testy są pominięte (Skipped)

**Rozwiązanie:**
- Testy szczegółów produktu wymagają produktów w bazie
- Dodaj dane testowe do bazy lub zignoruj te testy

### **Problem:** Brak przeglądarki Playwright

**Rozwiązanie:**
```powershell
pwsh bin/Debug/net10.0/playwright.ps1 install
```

---

## 📈 **Dodawanie nowych testów**

1. Utwórz nową klasę dziedziczącą po `BaseTest`
2. Dodaj atrybut `[TestFixture]`
3. Każdy test oznacz `[Test]`
4. Używaj metod pomocniczych:
   - `WaitForPageLoadAsync()` - czekaj na załadowanie
   - `TakeScreenshotAsync(name)` - zrób screenshot

Przykład:

```csharp
[TestFixture]
public class MyPageTests : BaseTest
{
    [Test]
    public async Task MyPage_ShouldLoad()
    {
        await Page.GotoAsync($"{BaseUrl}/my-page");
        await WaitForPageLoadAsync();
        
        await Expect(Page).ToHaveURLAsync(new Regex("/my-page"));
        
        await TakeScreenshotAsync("my_page");
    }
}
```

---

## 🎯 **Następne kroki**

### **Możliwe usprawnienia:**
- [ ] Dodać testy dla scenariusza pełnego zakupu (E2E flow)
- [ ] Dodać testy responsywności (mobile, tablet)
- [ ] Integracja z CI/CD (GitHub Actions)
- [ ] Visual regression testing (porównywanie screenshotów)
- [ ] Testy wydajności (PageSpeed, Lighthouse)
- [ ] Testy autoryzacji (login, register)
- [ ] Testy dla "Moje wypożyczenia"

---

## 📚 **Dokumentacja**

- [Playwright C# Docs](https://playwright.dev/dotnet/)
- [NUnit Documentation](https://docs.nunit.org/)
- [Playwright Best Practices](https://playwright.dev/docs/best-practices)

---

**🎉 Gotowe! Testy działają i możesz oglądać UI aplikacji w akcji!**

