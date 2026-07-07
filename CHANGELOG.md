# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### ✨ Added (Grudzień 2025)

#### 📱 Responsywne UI Mobile-First
- **Dual UI Strategy** - osobne widoki dla mobile (<768px) i desktop
- **Admin Panel:**
  - Responsywne wszystkie strony: Dashboard, Products, Rentals, Customers, Schedule, CompanySettings
  - Responsywne dialogi: CustomerEdit, CustomerRentals, IssueEquipment, ReturnEquipment
  - Mobile drawer z nawigacją
  - Dark mode z ThemeSwitcher
  - Sticky headers i kompaktowe karty na mobile
- **Client WASM:**
  - Mobile-first UI: Products, ProductDetails, Cart, Checkout, MyRentals
  - JS interop do wykrywania rozmiaru ekranu (`mobile-detection.js`)
  - Sticky bottom summaries (koszyk, checkout)
  - Kompaktowa siatka produktów 2-kolumnowa
  - Slidable filter panels na mobile
  - Usunięty TenantSelector z nagłówka (dostępny w filtrach)

#### 🗺️ Mapa wypożyczalni
- Nowa strona `/map` w Client z interaktywną mapą Leaflet
- LeafletMap component w Admin do wyświetlania lokalizacji
- JS interop dla Leaflet (`leaflet-map.js`, `leaflet-interop.js`)

#### 📍 Lokalizacja
- Dodano `City` i `Voivodeship` do modelu `Product`
- Dodano `City` i `Voivodeship` do modelu `CompanyInfo`
- Migracje DB: `AddCityAndVoivodeshipToProduct`, `AddCityAndVoivodeshipToCompanyInfo`
- API endpoint `/api/locations` - lista województw i miast
- Filtrowanie produktów po lokalizacji

#### 🕐 Wynajem godzinowy
- Dodano `HourlyPrice` do modelu `Product` - opcjonalna cena za godzinę
- Dodano `RentalType` enum (`Daily`, `Hourly`) do `Rental` i `RentalItem`
- Dodano `HoursRented` do `Rental` - liczba godzin przy wynajmie godzinowym
- Dodano `PricePerHour` do `RentalItem` - cena za godzinę w momencie wynajmu
- Zaktualizowano `PaymentCalculator` - obsługa kalkulacji cen godzinowych
- Zaktualizowano UI w Admin - wybór typu wynajmu przy tworzeniu wynajmu
- Zaktualizowano UI w Client (z responsywnym UI):
  - `Products.razor` - wyświetlanie ceny godzinowej, mobile/desktop views
  - `ProductDetails.razor` - wybór typu wynajmu, sticky "Dodaj do koszyka"
  - `Cart.razor` - zmiana typu wynajmu per pozycja, mobile summary
  - `Checkout.razor` - wyświetlanie i przekazywanie typu wynajmu do API

#### 🛠️ Usprawnienia developerskie
- Dodano profil uruchamiania "Admin + Client" w Visual Studio (`SportRentalHybrid.slnLaunch`)
- Wyłączono `launchBrowser` dla Admin - tylko Client otwiera przeglądarkę
- Wyłączono HTTPS redirect w Development - poprawka CORS dla lokalnego testowania
- Zaktualizowano `appsettings.Development.json` w Client - poprawny port API

#### 📡 Architektura (przypomnienie)
- **SportRental.Admin** - Blazor Server hostujący panel **ORAZ API dla klienta WASM**
- **SportRental.Client** - Blazor WASM łączący się z endpointami w Admin
- **SportRental.Api** - ⏸️ WYŁĄCZONY (przygotowany na przyszłość)
- **SportRental.MediaStorage** - ⏸️ WYŁĄCZONY (pliki w Azure Blob Storage)

#### 📚 Dokumentacja
- Zaktualizowano README.md - nowa architektura, responsywne UI
- Zaktualizowano ARCHITECTURE.md - dual UI strategy, aktualne diagramy
- Zaktualizowano DEVELOPER_GUIDE.md - mobile detection, JS interop
- Dodano informacje o nowych funkcjach (lokalizacja, mapa, responsive)

---

## [1.0.0] - Initial Release

### 🎉 Initial Release Features

#### ✨ Added
- **Multi-tenant Architecture** - Complete tenant isolation for rentals, customers, and products
- **Admin Panel** - Full-featured Blazor Server admin dashboard
  - Product management (CRUD operations)
  - Customer management
  - Rental tracking and management
  - Company information settings
  - Real-time dashboard with statistics
- **Public API** - RESTful API with minimal APIs pattern
  - Tenant-scoped endpoints with X-Tenant-Id header
  - Payment integration endpoints
  - Rental management endpoints
- **Client App** - Blazor WebAssembly public-facing client
  - Product catalog with filtering
  - Online booking system
  - Responsive TailwindCSS UI
- **Media Storage Service** - Dedicated microservice for media files
  - Chunked upload support
  - Automatic WebP conversion
  - Multiple thumbnail sizes
  - Azure Blob Storage integration
  - SQLite metadata storage
- **Payment Integration**
  - Stripe sandbox integration
  - Payment intents with deposit support
  - Webhook handling for async payment events
  - Refund support
  - Multi-currency support (PLN)
- **PDF Contract Generation**
  - QuestPDF-based contract generation
  - Company branding support
  - QR code integration
  - Professional invoice layout
- **Email System**
  - SMTP email sending
  - Rental confirmation emails
  - HTML email templates
  - Onet.pl integration tested
- **Security**
  - Azure Key Vault integration for secrets
  - JWT authentication
  - Role-based authorization
  - Secure password hashing
  - HTTPS enforcement
- **Database**
  - PostgreSQL for main data
  - SQLite for media metadata
  - Entity Framework Core migrations
  - Connection string encryption
- **Testing**
  - ~315 automated tests
  - Unit tests with xUnit
  - Integration tests with WebApplicationFactory
  - Blazor component tests with bUnit
  - Mock payment gateway for testing
  - Unit + integration + component (bUnit) test coverage
- **DevOps**
  - Manual local build/deploy (VS Publish to Azure App Service)
  - Health checks
  - Logging with Serilog
  - Performance monitoring

#### 📚 Documentation
- Comprehensive README with architecture diagrams
- API documentation
- Developer guide
- Testing guide
- Security guidelines
- Setup instructions
- Contributing guidelines

#### 🔒 Security
- All secrets stored in Azure Key Vault
- No hardcoded credentials
- Proper .gitignore configuration
- Security.md with responsible disclosure policy

### 🐛 Fixed
- N/A (initial release)

### 🔄 Changed
- N/A (initial release)

### 🗑️ Removed
- N/A (initial release)

---

## Release Notes Format

### Types of Changes
- `✨ Added` for new features
- `🔄 Changed` for changes in existing functionality
- `🗑️ Deprecated` for soon-to-be removed features
- `🗑️ Removed` for now removed features
- `🐛 Fixed` for any bug fixes
- `🔒 Security` for vulnerability fixes

---

[Unreleased]: https://github.com/DamianTarnowski/SportRental/compare/v1.0.0...HEAD
