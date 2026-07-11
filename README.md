<div align="center">

# 🏂 SportRental

### *Enterprise-Grade Multi-Tenant Sport Equipment Rental Platform*

**Engineered with \.NET 10 • Blazor • Azure • Stripe**

---

[![\.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Blazor](https://img.shields.io/badge/Blazor-Server%20%2B%20WASM-512BD4?style=for-the-badge&logo=blazor&logoColor=white)](https://learn.microsoft.com/aspnet/core/blazor/)
[![Azure](https://img.shields.io/badge/Azure-Key%20Vault%20%2B%20Blob-0078D4?style=for-the-badge&logo=microsoft-azure&logoColor=white)](https://azure.microsoft.com/)
[![PostgreSQL](https://img.shields.io/badge/PostgreSQL-14%2B-316192?style=for-the-badge&logo=postgresql&logoColor=white)](https://www.postgresql.org/)

[![Tests](https://img.shields.io/badge/tests-315%2B%20automated-00C853?style=for-the-badge&logo=checkmarx&logoColor=white)](#-testing--quality)
[![License](https://img.shields.io/badge/license-Proprietary-red?style=for-the-badge&logo=bookstack&logoColor=white)](#-license)
[![Status](https://img.shields.io/badge/status-production%20ready-00C853?style=for-the-badge&logo=statuspage&logoColor=white)](#-project-status)

</div>

---

## 🚀 **Quick Overview**

> **SportRental** is a **production-ready, enterprise-grade** multi-tenant platform for sport equipment rental businesses. Built with cutting-edge \.NET 10 technologies, it features complete **Stripe payment integration**, **Azure cloud services**, **automated PDF contracts**, and a stunning **Blazor UI**.

### **🎯 Perfect For:**
- 🏂 Ski & Snowboard Rental Shops
- 🚴 Bike Rental Companies
- 🏄 Water Sports Equipment Rentals
- ⛷️ Multi-Location Rental Chains
- 🏢 SaaS Rental Platforms

---

## ✨ **Key Features**

<table>
<tr>
<td width="50%">

### 🏢 **Multi-Tenant Architecture**
- ✅ Complete tenant isolation
- ✅ Per-tenant databases & storage
- ✅ Custom branding per tenant
- ✅ Scalable to 1000+ tenants

### 💳 **Payment Integration**
- ✅ **Stripe** sandbox & production
- ✅ Payment intents with deposits
- ✅ Webhook handling
- ✅ Automatic refunds
- ✅ Multi-currency support

### 📄 **Document Generation**
- ✅ Professional PDF contracts
- ✅ Barcode (Code 128) integration (QR deprecated)
- ✅ Company branding
- ✅ Digital signatures ready

</td>
<td width="50%">

### 🎨 **Modern UI/UX**
- ✅ Blazor Server admin panel
- ✅ Blazor WASM client app
- ✅ MudBlazor & TailwindCSS
- ✅ **📱 Mobile-First Dual UI** - osobne widoki mobile/desktop
- ✅ **🌙 Dark Mode** - przełącznik motywu
- ✅ **🗺️ Mapa Leaflet** - interaktywna mapa wypożyczalni
- ✅ Real-time updates with SignalR

### 🔒 **Enterprise Security**
- ✅ **Azure Key Vault** integration
- ✅ JWT authentication
- ✅ Role-based authorization
- ✅ **ZERO secrets in code**
- ✅ HTTPS enforcement

### 📧 **Communication**
- ✅ Email notifications (SMTP)
- ✅ Rental confirmations
- ✅ Payment receipts
- ✅ HTML templates

</td>
</tr>
</table>

---

## 🏗️ **Architecture**

<div align="center">

```mermaid
flowchart TB
    subgraph Client["🌐 Frontend Layer"]
        WASM["🎨 Blazor WASM<br/>Public Client"]
        Admin["⚙️ Blazor Server<br/>Admin Panel + API"]
    end
    
    subgraph Data["💾 Data Layer"]
        PostgreSQL[("🐘 PostgreSQL<br/>Main Database")]
        Blob["☁️ Azure Blob<br/>File Storage"]
    end
    
    subgraph External["🌍 External Services"]
        Stripe["💳 Stripe<br/>Payments"]
        KeyVault["🔑 Azure Key Vault<br/>Secrets"]
        SMTP["📧 SMTP<br/>Email"]
        SMSAPI["📱 SMSAPI<br/>SMS Notifications"]
    end
    
    WASM -->|REST + X-Tenant-Id| Admin
    Admin --> PostgreSQL
    Admin --> Blob
    Admin --> Stripe
    Admin --> KeyVault
    Admin --> SMTP
    Admin --> SMSAPI
    
    style WASM fill:#512BD4,stroke:#fff,stroke-width:2px,color:#fff
    style Admin fill:#512BD4,stroke:#fff,stroke-width:2px,color:#fff
    style PostgreSQL fill:#316192,stroke:#fff,stroke-width:2px,color:#fff
    style Stripe fill:#635BFF,stroke:#fff,stroke-width:2px,color:#fff
    style KeyVault fill:#FF6F00,stroke:#fff,stroke-width:2px,color:#fff
```

> **📝 Uwaga:** Aktualnie API dla klienta WASM jest hostowane w projekcie **SportRental.Admin** (Blazor Server). Projekt **SportRental.Api** jest wyłączony - przygotowany na przyszłość gdy będzie potrzeba osobnego serwera API. Projekt **SportRental.MediaStorage** również nie jest używany - pliki są przechowywane bezpośrednio w Azure Blob Storage.

</div>

---

## 📦 **Module Breakdown**

| Module | Description | Tech Stack | Status |
|--------|-------------|------------|--------|
| **🎨 SportRental.Admin** | Blazor Server admin panel + API dla klienta WASM | Blazor Server, MudBlazor, **📱 Dual UI** | ✅ Production |
| **📡 SportRental.Api** | empty placeholder folder (no code); former standalone API preserved in _DEPRECATED_SportRental.Api, excluded from the build | — | ⏸️ Disabled |
| **💻 SportRental.Client** | Blazor WebAssembly public client | Blazor WASM, TailwindCSS, **📱 Mobile-First** | ✅ Production |
| **📸 SportRental.MediaStorage** | Media microservice (obecnie wyłączone - Azure Blob) | Minimal APIs, SQLite | ⏸️ Disabled |
| **🔧 SportRental.Infrastructure** | EF Core, domain models, migrations | Entity Framework Core 9.0.9 | ✅ Production |
| **📦 SportRental.Shared** | Shared DTOs, components, HTTP clients | Razor Class Library | ✅ Production |
| **🧪 *.Tests** | Automated tests | xUnit, bUnit, Moq | ✅ Passing |

---

## 🎯 **Tech Stack**

<div align="center">

### **Backend**
![.NET](https://img.shields.io/badge/.NET%2010-512BD4?style=flat-square&logo=dotnet&logoColor=white)
![C#](https://img.shields.io/badge/C%23-239120?style=flat-square&logo=c-sharp&logoColor=white)
![Entity Framework](https://img.shields.io/badge/EF%20Core%209.0.9-512BD4?style=flat-square&logo=.net&logoColor=white)
![PostgreSQL](https://img.shields.io/badge/PostgreSQL-316192?style=flat-square&logo=postgresql&logoColor=white)
![SQLite](https://img.shields.io/badge/SQLite-07405E?style=flat-square&logo=sqlite&logoColor=white)

### **Frontend**
![Blazor](https://img.shields.io/badge/Blazor-512BD4?style=flat-square&logo=blazor&logoColor=white)
![MudBlazor](https://img.shields.io/badge/MudBlazor-594AE2?style=flat-square&logo=blazor&logoColor=white)
![TailwindCSS](https://img.shields.io/badge/Tailwind-38B2AC?style=flat-square&logo=tailwind-css&logoColor=white)
![SignalR](https://img.shields.io/badge/SignalR-512BD4?style=flat-square&logo=.net&logoColor=white)

### **Cloud & DevOps**
![Azure](https://img.shields.io/badge/Azure-0078D4?style=flat-square&logo=microsoft-azure&logoColor=white)

> 📝 **Uwagi dev:** na etapie lokalnym budujemy/uruchamiamy ręcznie (na laptopie) bez CI/CD w chmurze, żeby nie generować kosztów GitHub Actions. Pipeline’y CI/CD warto włączyć dopiero po przygotowaniu stałego środowiska serwerowego/budżetu na buildy.

### **Integrations**
![Stripe](https://img.shields.io/badge/Stripe-635BFF?style=flat-square&logo=stripe&logoColor=white)
![QuestPDF](https://img.shields.io/badge/QuestPDF-FF6B6B?style=flat-square&logo=adobe-acrobat-reader&logoColor=white)
![SMTP](https://img.shields.io/badge/SMTP-EA4335?style=flat-square&logo=gmail&logoColor=white)

</div>

---

## 🚀 **Quick Start**

### **Prerequisites**

- ✅ [\.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- ✅ [PostgreSQL 14+](https://www.postgresql.org/download/)
- ✅ [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli) (for Key Vault)
- ✅ [Node.js 18+](https://nodejs.org/) (for TailwindCSS)

### **⚡ 5-Minute Setup**

   ```bash
# 1️⃣ Clone the repository
git clone https://github.com/DamianTarnowski/SportRental.git
cd SportRental

# 2️⃣ Restore dependencies
dotnet restore

# 3️⃣ Setup database
cd SportRental.Admin
dotnet ef database update
cd ..

# 4️⃣ Configure Azure Key Vault (recommended)
az login
# Add your secrets to Key Vault (see SECURITY.md)

# 5️⃣ Run the services
# Opcja A: Visual Studio - użyj profilu "Admin + Client" (uruchamia oba projekty)
# Opcja B: Ręcznie w terminalu:
dotnet run --project SportRental.Admin --urls "http://localhost:5001"
dotnet run --project SportRental.Client --urls "http://localhost:5014"

# UWAGA: SportRental.Api i SportRental.MediaStorage są obecnie WYŁĄCZONE
# API jest hostowane w SportRental.Admin, pliki w Azure Blob Storage
```

**🎉 Done!** Open https://localhost:7142 for the admin panel.

📖 **Detailed setup guide:** [docs/QUICKSTART.md](docs/QUICKSTART.md)

---

## 🧪 **Testing & Quality**

### **315+ Automated Tests • Admin 303 · Client 6 · MediaStorage 6**

   ```bash
# Run all tests
dotnet test

# Results:
# ✅ SportRental.Admin.Tests:        303 tests
# ✅ SportRental.Client.Tests:         6 tests
# ✅ SportRental.MediaStorage.Tests:   6 tests
```

### **Test Coverage**

- ✅ **Unit Tests** - Business logic, services, validators
- ✅ **Integration Tests** - API endpoints, database operations
- ✅ **Component Tests** - Blazor components (bUnit)
- ✅ **E2E Tests** - Full user flows with WebApplicationFactory

### **Code Quality**

- ✅ `.editorconfig` with consistent formatting
- ✅ Roslyn analyzers enabled
- ✅ Warnings as errors in Release builds
- ✅ XML documentation on public APIs
- ✅ Nullable reference types enforced

---

## 📚 **Documentation**

<table>
<tr>
<td width="50%">

### 📖 **Core Documentation**
- 🏗️ [**Architecture**](doc/ARCHITECTURE.md) - System design & patterns
- 👨‍💻 [**Developer Guide**](doc/DEVELOPER_GUIDE.md) - Setup & workflow
- 📡 [**API Reference**](doc/API_DOCUMENTATION.md) - Endpoint documentation
- 🗺️ [**Roadmap**](doc/ROADMAP.md) - Future plans & milestones

### 🎨 **Feature Guides**
- 📸 [**Media Features**](doc/MEDIA_FEATURES.md) - Image processing
- 🏢 [**Company Info**](doc/guides/ADMIN_PANEL_COMPANY_INFO.md) - Tenant config
- 💰 [**Valuation**](doc/VALUATION.md) - Project analysis

</td>
<td width="50%">

### ⚙️ **Setup Guides**
- 🔑 [**Azure Key Vault**](doc/setup/AZURE_KEY_VAULT_SETUP.md) - Secrets management
- ☁️ [**Azure Blob Storage**](doc/setup/AZURE_BLOB_STORAGE_SETUP.md) - Cloud storage
- 📧 [**Email Setup**](doc/setup/ONET_EMAIL_SETUP.md) - SMTP configuration
- 💳 [**Stripe Sandbox**](doc/setup/STRIPE_SANDBOX_GUIDE.md) - Payment testing
- ⚖️ [**Legal Documents**](doc/setup/LEGAL_DOCUMENTS.md) - Operator data and document versioning

### 🧪 **Testing**
- 🧪 [**Testing Guide**](doc/TESTING_GUIDE.md) - Complete testing docs
- 🚀 [**Quick Start**](docs/QUICKSTART.md) - 5-minute setup

</td>
</tr>
</table>

---

## 🗺️ **Roadmap**

### **✅ Completed (2025)**
- ✅ Multi-tenant architecture
- ✅ Blazor Server admin panel + API
- ✅ Blazor WASM client
- ✅ Stripe payment integration (Checkout Sessions)
- ✅ Azure Key Vault integration
- ✅ Azure Blob Storage (zdjęcia produktów)
- ✅ PDF contract generation (QuestPDF)
- ✅ Email notifications (SMTP)
- ✅ SMS notifications (SMSAPI.pl)
- ✅ **Wynajem godzinowy** - obsługa HourlyPrice, RentalType, HoursRented
- ✅ Reservation holds (tymczasowe rezerwacje w koszyku)
- ✅ Customer session management
- ✅ Visual Studio multi-project launch (Admin + Client)
- ✅ **📱 Mobile-First Responsive UI** - dual UI strategy (mobile/desktop)
- ✅ **🌙 Dark Mode** - przełącznik motywu w Admin
- ✅ **🗺️ Mapa wypożyczalni** - Leaflet integration
- ✅ **📍 Lokalizacja** - City/Voivodeship filtering

### **🚧 In Progress / Planned**
- ℹ️ CI/CD and containerization are intentionally out of scope — build and deploy are done locally (VS Publish / az webapp deploy)
- 🚧 Application Insights monitoring
- 🚧 CloudFlare CDN integration
- 🚧 Reaktywacja SportRental.Api jako osobny serwer (gdy potrzeba skalowania)
- 🚧 Reaktywacja SportRental.MediaStorage (gdy zmiana hostingu z Azure)

### **📅 Planned (2025-2026)**
- 📅 Rate limiting & throttling
- 📅 Production Stripe activation
- 📅 Performance optimization
- 📅 MAUI mobile app
- 📅 Analytics dashboards
- 📅 Multi-language support

---

## 🔒 **Security**

> **🔐 ZERO secrets in code!**

This project uses **Azure Key Vault** for all sensitive data:
- 🔑 Database connection strings
- 🔑 API keys (Stripe, SMTP)
- 🔑 JWT signing keys
- 🔑 Azure storage credentials

**📖 Read [SECURITY.md](SECURITY.md) for complete security guidelines.**

---

## 📊 **Project Status**

| Component | Status | Details |
|-----------|--------|---------|
| 🎨 **Admin Panel** | ✅ **Production Ready** | Complete UI, all features working |
| 📡 **REST API** | ✅ **Production Ready** | Hosted in-process by SportRental.Admin (MapSportRentalApi/MapControllers); standalone SportRental.Api is disabled/empty |
| 💻 **Client App** | ✅ **Production Ready** | Responsive UI, checkout flow |
| 📸 **Media Storage** | ⏸️ **Optional/Idle** | Files go directly to Azure Blob; MediaStorage microservice not used in production |
| 💳 **Payments** | ✅ **Sandbox Ready** | Stripe test mode integrated |
| 🧪 **Tests** | ✅ **315+ Automated** | Admin 303 · Client 6 · MediaStorage 6, high coverage |
| 📚 **Documentation** | ✅ **Complete** | Comprehensive guides & API docs |

---

## 💼 **License**

### **📜 Proprietary License - Commercial Use Only**

> **⚠️ This software is proprietary and protected by copyright law.**

#### **🚫 You MAY NOT:**
- ❌ Use this software for commercial purposes without a license
- ❌ Copy, modify, or distribute this software
- ❌ Create derivative works based on this software
- ❌ Use this software in production environments
- ❌ Remove or modify copyright notices

#### **✅ You MAY:**
- ✅ View the source code for educational purposes
- ✅ Report bugs and security vulnerabilities
- ✅ Discuss the architecture and implementation

#### **💰 Commercial Licensing**

**Interested in using SportRental for your business?**

For commercial licensing, custom development, or technical support:

📧 **Contact:** hdtdtr@gmail.com

**We offer:**
- 💼 **Commercial Licenses** - Full rights to use in your business
- 🛠️ **Custom Development** - Tailored features for your needs
- 🤝 **Technical Support** - Priority support & maintenance
- 🎓 **Training & Consulting** - Get up to speed quickly

**Pricing:** Contact for a quote based on your requirements.

---

**Copyright © 2025 Damian Tarnowski. All Rights Reserved.**

---

## 🤝 **Contributing**

While this is **proprietary software**, we welcome:
- 🐛 **Bug Reports** - Help us improve quality
- 💡 **Feature Suggestions** - Share your ideas
- 🔒 **Security Reports** - Responsible disclosure

Please see [SECURITY.md](SECURITY.md) for security vulnerability reporting.

---

## 📞 **Contact & Support**

**For licensing inquiries:**
- 📧 Email: hdtdtr@gmail.com
- 💼 GitHub: [DamianTarnowski](https://github.com/DamianTarnowski)

---

<div align="center">

**🏂 Built with ❤️ using \.NET 10 & Blazor**

[![.NET](https://img.shields.io/badge/.NET-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Blazor](https://img.shields.io/badge/Blazor-512BD4?style=for-the-badge&logo=blazor&logoColor=white)](https://blazor.net/)
[![Azure](https://img.shields.io/badge/Azure-0078D4?style=for-the-badge&logo=microsoft-azure&logoColor=white)](https://azure.microsoft.com/)

---

**⭐ If you're interested in licensing SportRental, please get in touch!**

</div>

