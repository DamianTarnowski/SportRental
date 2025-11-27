# Poprawki aplikacji Client WASM dla produkcji

## 🔍 Zidentyfikowane problemy

### 1. **Nieprawidłowa konfiguracja BaseUrl**
- **Problem**: `appsettings.json` w aplikacji Client miał hardcoded `https://localhost:7142`
- **Skutek**: W środowisku produkcyjnym (Docker) aplikacja WASM próbowała łączyć się z localhost zamiast z prawdziwym API
- **Rozwiązanie**: 
  - Ustawiono pustą wartość `BaseUrl: ""` w `appsettings.json` (dla produkcji)
  - Utworzono `appsettings.Development.json` z `BaseUrl: "https://localhost:7142"` (dla lokalnego developmentu)
  - Dzięki temu w produkcji aplikacja używa tego samego hosta (poprzez nginx reverse proxy)

### 2. **Ograniczona konfiguracja CORS w API**
- **Problem**: CORS w `SportRental.Api/Program.cs` był skonfigurowany tylko dla localhost
- **Skutek**: Requesty z produkcyjnego środowiska mogły być blokowane
- **Rozwiązanie**: Rozszerzona konfiguracja CORS z obsługą różnych środowisk:
  ```csharp
  if (isDevelopment)
  {
      // Development: Allow localhost
      policy.WithOrigins(...).AllowAnyHeader().AllowAnyMethod();
  }
  else
  {
      // Production: Allow same-origin (nginx proxy)
      policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
  }
  ```

### 3. **Nieprawidłowe wymaganie Tenant-Id dla klientów**
- **Problem**: API wymagało nagłówka `X-Tenant-Id` dla wszystkich endpointów, w tym dla produktów
- **Skutek**: 
  - Klienci musieli wybierać wypożyczalnię przed zobaczeniem produktów
  - Nie mogli przeglądać oferty ze wszystkich wypożyczalni
  - API zwracał błąd 400 Bad Request
- **Rozwiązanie**: 
  - Zmieniono logikę biznesową: **klienci domyślnie widzą produkty ze wszystkich wypożyczalni**
  - Wybór wypożyczalni to **opcjonalny filtr**, nie wymagane ustawienie
  - Endpoint `/api/products` nie wymaga nagłówka `X-Tenant-Id`:
    - Bez nagłówka → produkty ze **wszystkich** wypożyczalni
    - Z nagłówkiem → produkty tylko z **wybranej** wypożyczalni
  - Dodano `TenantId` do `ProductDto`, żeby klient wiedział z jakiej wypożyczalni jest produkt
  - Zmieniono UI `TenantSelector` na opcjonalny filtr z przyciskiem "Wszystkie"

## 📝 Wprowadzone zmiany

### Pliki utworzone:
1. **`SportRental.Client/wwwroot/appsettings.Development.json`**
   - Konfiguracja dla lokalnego developmentu
   - BaseUrl wskazuje na localhost:7142

### Pliki zmodyfikowane:

1. **`SportRental.Client/wwwroot/appsettings.json`**
   - Zmieniono `BaseUrl` z `"https://localhost:7142"` na `""`
   - Teraz aplikacja używa tego samego hosta co strona

2. **`SportRental.Api/Program.cs`**
   - Rozszerzona konfiguracja CORS dla środowisk Development i Production
   - Production pozwala na wszystkie originy (dla nginx reverse proxy)

3. **`SportRental.Shared/Models/ProductDto.cs`**
   - Dodano `public Guid TenantId { get; set; }`
   - Klient teraz wie z jakiej wypożyczalni jest każdy produkt

4. **`SportRental.Client/Program.cs`**
   - Zmieniono komentarze: TenantId jest **opcjonalny**
   - Aplikacja działa bez wyboru tenanta (pokazuje wszystkie produkty)

5. **`SportRental.Client/Pages/SelectTenant.razor`**
   - Usunięto automatyczne przekierowanie jeśli tenant już wybrany
   - Pozwala użytkownikowi zmienić wybór

6. **`SportRental.Client/Components/TenantSelector.razor`**
   - Zmieniono z "Wybierz wypożyczalnię" na "Filtruj"
   - Dodano przycisk "Wszystkie" do wyczyszczenia filtra
   - Pokazuje status: "Wszystkie wypożyczalnie" lub "Filtr wypożyczalni: [nazwa]"

## 🎯 Przepływ działania po zmianach

### Scenariusz 1: Pierwszy raz użytkownika (bez wyboru wypożyczalni)
1. Użytkownik wchodzi na stronę
2. `Program.cs` próbuje załadować TenantId z LocalStorage (brak) - **to OK**
3. Użytkownik klika "Przeglądaj sprzęt" → `/products`
4. Request do API **bez** nagłówka `X-Tenant-Id`
5. API zwraca produkty ze **wszystkich wypożyczalni** ✅
6. Użytkownik widzi całą ofertę z informacją z jakiej wypożyczalni jest każdy produkt

### Scenariusz 2: Użytkownik chce filtrować po wypożyczalni
1. Użytkownik klika "Filtruj" w TenantSelector
2. Przechodzi na `/select-tenant`
3. Wybiera konkretną wypożyczalnię
4. TenantId zapisany w LocalStorage
5. Strona odświeżana (`forceLoad: true`)
6. `Program.cs` ładuje TenantId i ustawia nagłówek `X-Tenant-Id`
7. Request do API **z** nagłówkiem `X-Tenant-Id`
8. API zwraca produkty tylko z **wybranej wypożyczalni** ✅
9. Użytkownik może kliknąć "Wszystkie" żeby wrócić do pełnej oferty

### Scenariusz 3: Powracający użytkownik z filtrem
1. Użytkownik wchodzi na stronę
2. `Program.cs` ładuje TenantId z LocalStorage ✅
3. Nagłówek `X-Tenant-Id` jest ustawiony
4. Użytkownik widzi produkty z ostatnio wybranej wypożyczalni
5. Może w dowolnym momencie kliknąć "Wszystkie" żeby zobaczyć całą ofertę

## 🚀 Wdrożenie

### Lokalne środowisko (Development)
```bash
# Aplikacja automatycznie użyje appsettings.Development.json
cd SportRental.Client
dotnet run
```

### Środowisko produkcyjne (Docker)
```bash
# Build i uruchomienie całego stacku
docker-compose up -d --build

# Aplikacja Client będzie dostępna przez nginx:
# http://localhost:80          # główna strona (client)
# http://localhost:80/api/     # API
# http://localhost:80/admin/   # panel administracyjny
```

## ✅ Korzyści

1. **Poprawne działanie w produkcji**: Aplikacja WASM prawidłowo łączy się z API przez nginx reverse proxy
2. **Bezpieczne środowiska**: Różne konfiguracje dla development i production
3. **Lepsze UX**: Użytkownik **natychmiast widzi wszystkie produkty** bez konieczności wyboru wypożyczalni
4. **Opcjonalne filtrowanie**: Użytkownik może filtrować produkty po konkretnej wypożyczalni jeśli chce
5. **Zgodność z architekturą multi-tenant**: System wspiera zarówno widok globalny jak i per-tenant
6. **Przejrzystość**: Każdy produkt ma informację z jakiej wypożyczalni pochodzi

## 📋 Checklist przed wdrożeniem

- [x] Utworzono `appsettings.Development.json`
- [x] Poprawiono `appsettings.json` (BaseUrl pusty)
- [x] Rozszerzono CORS w API
- [x] Zmieniono endpoint `/api/products` - wspiera brak tenant-id
- [x] Dodano `TenantId` do `ProductDto`
- [x] Zmieniono UI `TenantSelector` na opcjonalny filtr
- [x] Zaktualizowano logikę w `Program.cs`
- [ ] Przetestowano lokalnie
- [ ] Przetestowano w Docker
- [ ] Zweryfikowano że produkty się ładują bez wyboru tenanta
- [ ] Zweryfikowano filtrowanie po tenantcie
- [ ] Zweryfikowano przycisk "Wszystkie"

## 🔧 Potencjalne dalsze usprawnienia

1. **Optymalizacja CORS**: W produkcji zamiast `AllowAnyOrigin()` można użyć konkretnych domen
2. **Wyświetlanie nazwy wypożyczalni**: Dodać nazwę wypożyczalni do widoku produktu (obecnie tylko TenantId w DTO)
3. **Lepsze komunikaty błędów**: Gdy API jest niedostępne
4. **Timeout handling**: Gdy request do API trwa zbyt długo
5. **Retry logic**: Automatyczne ponowienie próby w przypadku błędu sieci
6. **Geolokalizacja**: Automatyczne sortowanie wypożyczalni według odległości od użytkownika
7. **Zapamiętanie preferencji**: Domyślny filtr według ostatnio używanej wypożyczalni

