# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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
  - 356+ automated tests
  - Unit tests with xUnit
  - Integration tests with WebApplicationFactory
  - Blazor component tests with bUnit
  - Mock payment gateway for testing
  - >80% code coverage
- **DevOps**
  - GitHub Actions CI/CD pipeline
  - Docker support
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
