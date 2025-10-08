# 📜 Development Scripts

> Helper scripts for database management, testing, and development workflows

## 📁 Structure

```
scripts/
├─ database/           # Database setup and seeding
│  ├─ create-test-database.sql
│  ├─ reset-and-seed-database.ps1
│  └─ setup-test-database.ps1
│
├─ testing/            # Testing automation scripts
│  ├─ RUN_ALL_FOR_STRIPE_TEST.ps1
│  ├─ TEST_EMAIL_FLOW.ps1
│  ├─ test-onet-email.ps1
│  └─ test-stripe-payment.ps1
│
└─ start-stripe-webhooks.ps1  # Stripe webhook forwarding

```

---

## 🗄️ Database Scripts

### `database/reset-and-seed-database.ps1`

**Purpose:** Reset database and seed with test data

**Usage:**
```powershell
./scripts/database/reset-and-seed-database.ps1
```

**What it does:**
1. Drops the database
2. Applies migrations
3. Runs application (auto-seeding in Development)

---

### `database/setup-test-database.ps1`

**Purpose:** Initial database setup for testing

**Usage:**
```powershell
./scripts/database/setup-test-database.ps1
```

---

### `database/create-test-database.sql`

**Purpose:** SQL script for manual database creation

**Usage:**
```sql
psql -U postgres -f ./scripts/database/create-test-database.sql
```

---

## 🧪 Testing Scripts

### `testing/RUN_ALL_FOR_STRIPE_TEST.ps1`

**Purpose:** Run all services for Stripe payment testing

**Usage:**
```powershell
./scripts/testing/RUN_ALL_FOR_STRIPE_TEST.ps1
```

**Starts:**
- SportRental.Api (Backend)
- SportRental.Client (Frontend)
- Stripe webhooks

---

### `testing/TEST_EMAIL_FLOW.ps1`

**Purpose:** Test email sending and receiving

**Usage:**
```powershell
./scripts/testing/TEST_EMAIL_FLOW.ps1
```

---

### `testing/test-onet-email.ps1`

**Purpose:** Test Onet SMTP configuration

**Usage:**
```powershell
./scripts/testing/test-onet-email.ps1
```

---

### `testing/test-stripe-payment.ps1`

**Purpose:** Automated Stripe payment flow testing

**Usage:**
```powershell
./scripts/testing/test-stripe-payment.ps1
```

---

## 💳 Stripe Scripts

### `start-stripe-webhooks.ps1`

**Purpose:** Start Stripe CLI webhook forwarding

**Usage:**
```powershell
./scripts/start-stripe-webhooks.ps1
```

**What it does:**
1. Starts `stripe.exe` webhook forwarding
2. Forwards webhooks from Stripe to local API
3. Provides webhook signing secret for `appsettings.json`

**Requirements:**
- `stripe.exe` in root directory
- Stripe account configured
- API running on `https://localhost:7142`

---

## 📚 Related Documentation

- [TESTING_GUIDE.md](../doc/TESTING_GUIDE.md) - Complete testing documentation
- [DEVELOPER_GUIDE.md](../doc/DEVELOPER_GUIDE.md) - Developer setup guide
- [setup/STRIPE_SANDBOX_GUIDE.md](../doc/setup/STRIPE_SANDBOX_GUIDE.md) - Stripe configuration

---

## 💡 Tips

### Quick Database Reset:
```powershell
# One-liner
./scripts/database/reset-and-seed-database.ps1
```

### Full Stripe Test:
```powershell
# 3 terminals needed:
# Terminal 1:
dotnet run --project SportRental.Api

# Terminal 2:
dotnet run --project SportRental.Client

# Terminal 3:
./scripts/start-stripe-webhooks.ps1
```

### Email Testing:
```powershell
# Test sending
./scripts/testing/TEST_EMAIL_FLOW.ps1

# Test Onet SMTP
./scripts/testing/test-onet-email.ps1
```

---

**Last updated:** 2025-10-07  
**Maintained by:** Development Team
