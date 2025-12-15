# SportRental.Api — dokumentacja projektu

> **⚠️ UWAGA: Ten projekt jest obecnie WYŁĄCZONY (grudzień 2025)**
>
> API dla klienta WASM jest hostowane w projekcie **SportRental.Admin**.
> Ten projekt jest przygotowany na przyszłość gdy będzie potrzeba osobnego serwera API (np. skalowanie, microservices).
>
> Aby uruchomić aplikację, użyj profilu **"Admin + Client"** w Visual Studio lub uruchom:
> - `SportRental.Admin` (panel + API) na `http://localhost:5001`
> - `SportRental.Client` (WASM) na `http://localhost:5014`

---

## Archiwalna dokumentacja (dla przyszłego użycia)

## TL;DR / Quick Start
- Uruchom Postgresa i ustaw connection string (z hasłem) lub `ConnectionStrings__DefaultConnection`.
- Migracje (jeśli potrzeba):
  ```powershell
  dotnet ef database update --project "C:\\Users\\hdtdt\\source\\repos\\SportRental\\SportRental.Infrastructure\\SportRental.Infrastructure.csproj" --startup-project "SportRental.Api\\SportRental.Api.csproj"
  ```
- HTTPS certyfikaty: `dotnet dev-certs https --trust`
- Start:
  ```powershell
  dotnet run --project "SportRental.Api\\SportRental.Api.csproj" --launch-profile https
  dotnet run --project "SportRental.Client\\SportRental.Client.csproj" --launch-profile https
  ```
- Swagger: `https://localhost:7142/swagger` → Authorize → `X-Tenant-Id = 00000000-0000-0000-0000-000000000000`

Ten projekt stanowi nowe API oparte o Minimal API dla rozwiązania SportRentalHybrid. Został połączony ze "starym" modelem domenowym i migracjami w celu kontynuacji rozwoju zgodnie z Clean Architecture i (docelowo) CQRS.

## Lokalizacja projektów
- Nowe API: `SportRental.Api/`
- Klient (Blazor WASM): `SportRental.Client/`
- Współdzielone DTO/Services: `SportRental.Shared/`
- Istniejące projekty domenowe i infrastruktura (poza tą solucją) — dodane jako referencje:
  - Domain: `C:\Users\hdtdt\source\repos\SportRental\SportRental.Domain`
  - Infrastructure (DbContext, migracje): `C:\Users\hdtdt\source\repos\SportRental\SportRental.Infrastructure`
- Stary projekt referencyjny (źródło logiki i modeli do przepisania): `BlazorApp3/` w tej solucji:
  - Endpointy i logika: `BlazorApp3/Api/Endpoints.cs`
  - Encje domenowe (stare): `BlazorApp3/Data/Domain/`

## Co zostało zrobione
- Utworzenie projektu `SportRental.Api` i dodanie referencji do:
  - `SportRental.Infrastructure` (DbContext + migracje)
  - `SportRental.Shared` (DTO i serwisy klienta)
- Konfiguracja EF Core pod PostgreSQL (Npgsql):
  - Connection string w `SportRental.Api/appsettings.Development.json` ustawiony na "stary":
    - `Host=localhost;Port=5432;Database=sportrental;Username=postgres`
  - Provider w `Program.cs`: `UseNpgsql(...)`
  - Design-time factory w `Infrastructure` również używa `UseNpgsql`
- Dodane endpointy Minimal API w `SportRental.Api/Program.cs`:
  - `GET /api/products` — lista produktów
  - `GET /api/products/{id}` — produkt po Id
  - `POST /api/holds` — tworzenie rezerwacji tymczasowej
  - `DELETE /api/holds/{id}` — usunięcie holda
  - `POST /api/rentals` — utworzenie wynajmu (wyliczanie sumy)
  - `DELETE /api/rentals/{id}` — anulowanie wynajmu
  - `GET /api/my-rentals` — lista wynajmów z filtrami (status/from/to)
- Multi-tenancy: wymagany nagłówek `X-Tenant-Id` (GUID). Middleware zwraca 400, jeśli brak/niepoprawny.
- CORS: skonfigurowany pod lokalne profile klienta (porty `7083/5014`, oraz dodatkowe: `5002/5173`).
- Swagger: dostępny w trybie Development.
- Klient: `SportRental.Client/Program.cs` ustawiony na BaseUrl `https://localhost:7142` oraz nagłówek `X-Tenant-Id` (tymczasowa wartość GUID dla DEV).

## Jak uruchomić lokalnie
1) Upewnij się, że PostgreSQL działa i connection string jest poprawny. Jeśli potrzebne hasło, dodaj `Password=...` do `appsettings.Development.json` lub ustaw zmienną środowiskową:
   - `ConnectionStrings__DefaultConnection="Host=...;Port=...;Database=...;Username=...;Password=..."`
2) Migracje EF (o ile baza nie jest aktualna):
```
 dotnet ef database update \
   --project "C:\\Users\\hdtdt\\source\\repos\\SportRental\\SportRental.Infrastructure\\SportRental.Infrastructure.csproj" \
   --startup-project "SportRental.Api\\SportRental.Api.csproj"
```
3) Uruchom API (https profil):
```
 dotnet run --project "SportRental.Api\\SportRental.Api.csproj" --launch-profile https
```
4) Uruchom klienta (https profil):
```
 dotnet run --project "SportRental.Client\\SportRental.Client.csproj" --launch-profile https
```
5) Testy:
- Swagger: `https://localhost:7142/swagger`
- W żądaniach dodawaj nagłówek: `X-Tenant-Id: 00000000-0000-0000-0000-000000000000` (DEV)

## Zależności i wersje
- .NET: `net9.0`
- EF Core provider: `Npgsql.EntityFrameworkCore.PostgreSQL`
- Swagger: `Swashbuckle.AspNetCore`

## Co dalej (plan migracji i rozwoju)
- [Model i migracje]
  - Uzupełnić model domenowy o brakujące encje (jeśli potrzebne): `CompanyInfo`, `EmployeePermissions`, `ProductCategory`, `TenantUser` itp. (zgodnie ze starym projektem)
  - Zweryfikować indeksy/klucze w `ApplicationDbContext` względem snapshotu migracji (uniknąć rozjazdów)
- [API — funkcjonalności do przepisania ze starego projektu `BlazorApp3/Api/Endpoints.cs`]
  - Generowanie kontraktów/PDF (`GET /api/contracts/{rentalId}`) i zwracanie linku `ContractUrl`
  - Obsługa wysyłki SMS/e-mail (powiadomienia o rezerwacji/aktywacji)
  - Upload logo tenanta i obrazów produktów
  - Anulowanie wynajmu z przyczynami/statusami pośrednimi
  - Zaawansowana walidacja (reguły biznesowe co do dostępności produktów, nakładania terminów)
- [Jakość i bezpieczeństwo]
  - Idempotencja `POST /api/rentals` po `IdempotencyKey` — zwracanie 409 przy duplikatach
  - Spójny model błędów (`ProblemDetails`), globalny handler wyjątków
  - Autoryzacja i pobieranie TenantId z tokena (usunięcie DEV fallbacku)
  - Stronicowanie/sortowanie/filtry dla list (produkty, wynajmy)
- [Architektura]
  - (Docelowo) CQRS + MediatR, rozdział komend/zapytania, operacje w handlerach
  - Integracje: MassTransit/RabbitMQ + Outbox pattern (publikacja zdarzeń domenowych)
- [DevEx]
  - Profile uruchomieniowe wielostartowe (Client + Api) w VS
  - Kolekcja Postman/Thunder Client, opis scenariuszy testowych

## Notatki projektowe
- Wymagany nagłówek `X-Tenant-Id` dla każdego wywołania API.
- Porty (launchSettings):
  - API: `https://localhost:7142` (http: `5242`)
  - Client: `https://localhost:7083` (http: `5014`)
- Stare źródła do przeglądu i przepisywania:
  - Endpointy: `BlazorApp3/Api/Endpoints.cs`
  - Encje: `BlazorApp3/Data/Domain/`

## Pytania/Decyzje
- Czy trzymamy hasło do DB w pliku `appsettings.Development.json`, czy zawsze przez zmienną środowiskową?
- Preferowane źródło TenantId (nagłówek vs token)?
- Zakres i kolejność przepisywania endpointów ze starego projektu.

---

## Użycie Swagger i nagłówka X-Tenant-Id
- Otwórz: `https://localhost:7142/swagger` (lub `http://localhost:5242/swagger`)
- Kliknij „Authorize” i wprowadź:
  - Key: `X-Tenant-Id`
  - Value: `00000000-0000-0000-0000-000000000000` (DEV)
- Po autoryzacji Swagger doda nagłówek do wszystkich wywołań.

## Uruchomienie wieloprojektowe (API + Klient)
- Visual Studio: ustaw „Multiple startup projects” dla `SportRental.Api` i `SportRental.Client` (profile https).
- CLI:
  - API: `dotnet run --project "SportRental.Api/SportRental.Api.csproj" --launch-profile https`
  - Client: `dotnet run --project "SportRental.Client/SportRental.Client.csproj" --launch-profile https`

## Konfiguracja środowiska i HTTPS
- Dev certyfikaty (Windows/PowerShell):
  ```powershell
  dotnet dev-certs https --trust
  ```
- Connection string (preferuj zmienną środowiskową):
  ```powershell
  $env:ConnectionStrings__DefaultConnection = "Host=localhost;Port=5432;Database=sportrental;Username=postgres;Password=..."
  ```
- Priorytet: zmienna środowiskowa > appsettings.Development.json

## Klient: wstrzykiwanie tenantId i BaseUrl
- Plik: `SportRental.Client/Program.cs`
  - `apiService.SetBaseUrl("https://localhost:7142")`
  - `apiService.SetTenantId(Guid.Parse("00000000-0000-0000-0000-000000000000"))`
- Docelowo pobieraj `TenantId` dynamicznie (profil użytkownika/ustawienia).

## Skrót endpointów (dokumentacja szczegółowa w Swagger)
- `GET /api/products` — 200: `List<ProductDto>`
- `GET /api/products/{id}` — 200: `ProductDto`, 404
- `POST /api/holds` — 200: `CreateHoldResponse`
- `DELETE /api/holds/{id}` — 204, 404
- `GET /api/my-rentals` — 200: `List<MyRentalDto>` (query: `status`, `from`, `to`)
- `POST /api/rentals` — 200: `RentalResponse`
- `DELETE /api/rentals/{id}` — 204, 404

## Middleware Tenancy — wyjątki
- Wymagany `X-Tenant-Id` dla wszystkich ścieżek poza: `/swagger`, `/` oraz żądaniami `OPTIONS` (preflight CORS).

## Troubleshooting
- __HTTPS ERR_CONNECTION_REFUSED__:
  - Uruchom `dotnet dev-certs https --trust`
  - Upewnij się, że port `7142` nie jest zajęty (ew. zmień profil lub użyj HTTP do testu: `--launch-profile http` → `http://localhost:5242/swagger`)
- __400 Missing or invalid X-Tenant-Id__:
  - W Swagger kliknij „Authorize” i dodaj poprawny GUID; w kliencie ustaw `ApiService.SetTenantId(...)`
- __500/DB connection__:
  - Zweryfikuj connection string/hasło; uruchom migracje `dotnet ef database update ...`
- __Mixed content (blokada przeglądarki)__:
  - API i Client muszą działać na HTTPS (napraw certyfikat); unikaj mieszania HTTP i HTTPS

## Po restarcie konwersacji — co robić dalej
- __Cel nadrzędny__: Synchronizacja modeli EF i migracji, rozwój Rentals/Holds/Products + multi-tenant, integracja klienta.
- __Kolejność prac__:
  1. Sprawdź DB (Postgres) i connection string; odpal API + Klienta jak w Quick Start.
  2. Jeśli potrzeba nowych funkcji, przepisuj ze starego projektu:
     - Plik: `BlazorApp3/Api/Endpoints.cs` (źródło logiki)
     - Encje referencyjne: `BlazorApp3/Data/Domain/`
  3. Najbliższe zadania backend:
     - Idempotencja w `POST /api/rentals` (zwracaj 409 przy duplikacie `IdempotencyKey`).
     - Globalny handler błędów + `ProblemDetails`.
     - Autoryzacja (wydobywanie TenantId z tokena) i usunięcie DEV fallbacku.
     - Paginacja/filtrowanie w listach.
  4. DevEx: Skonfiguruj Multiple Startup Projects (API + Client) w VS; przygotuj kolekcję Postman/Thunder.
  5. Utrzymanie: dopisuj brakujące XML summary do DTO, aby wzbogacić Swagger.





