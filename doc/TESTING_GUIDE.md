# 🧪 Testing Guide

> Complete testing documentation for SportRental project

## 📋 Quick Navigation

- [Quick Start: Stripe Testing](#-quick-start-stripe-testing)
- [Test Data Seeding](#-test-data-seeding)
- [E2E Testing Setup](#-e2e-testing-setup)
- [Test Scenarios](#-test-scenarios)
- [Troubleshooting](#-troubleshooting)

---

## 🚀 Quick Start: Stripe Testing

### ⚡ 3 Steps to Working Payments

#### **STEP 1: Run everything (3 terminals)**

**Terminal 1 - Backend API:**
```powershell
cd SportRentalHybrid
dotnet run --project SportRental.Admin --urls http://localhost:5001
```
✅ **API ready at:** `http://localhost:5001`

**Terminal 2 - Frontend Client:**
```powershell
cd SportRentalHybrid
dotnet run --project SportRental.Client
```
✅ **Client ready at:** `http://localhost:5014`

**Terminal 3 - Stripe Webhooks:**
```powershell
cd SportRentalHybrid
./scripts/start-stripe-webhooks.ps1
```

**IMPORTANT:** Copy the webhook signing secret (whsec_...) to Azure Key Vault:
```bash
az keyvault secret set \
  --vault-name YOUR-VAULT-NAME \
  --name "Stripe--WebhookSecret" \
  --value "whsec_..."
```

Then **RESTART API** (Terminal 1)

✅ **Stripe CLI ready!**

---

#### **STEP 2: Test flow in browser**

1. **Open:** http://localhost:5014
2. **Add product** to cart
3. **Go to:** http://localhost:5014/checkout
4. **Fill customer data:**
   - Name: Jan Testowy
   - Email: test@example.com
   - Phone: +48123456789
5. **Click:** "Potwierdź rezerwację"

✅ **You'll be redirected to Stripe Checkout!**

---

#### **STEP 3: Use test card**

On Stripe Checkout page:

```
Card number:    4242 4242 4242 4242
Expiry:         12/34 (or any future date)
CVC:            123
Postal code:    12345
```

**Click "Pay"**

✅ **Success!** Redirected to `/checkout/success`

---

### 📊 What You'll See

**In browser:**
```
✅ Checkout page → Stripe Checkout → Success page!
```

**In API terminal (Terminal 1):**
```
info: Stripe webhook received: checkout.session.completed
info: PaymentIntent succeeded: pi_3...
```

**In Stripe terminal (Terminal 3):**
```
✅ checkout.session.completed [evt_1...]
✅ payment_intent.succeeded [evt_2...]
```

---

### 🎯 Stripe Test Cards

| Card | Behavior |
|------|----------|
| `4242 4242 4242 4242` | ✅ Success |
| `4000 0000 0000 0002` | ❌ Declined |
| `4000 0025 0000 3155` | ⏳ Requires 3D Secure |

Full list: https://stripe.com/docs/testing

---

### ✅ Pre-test Checklist

- [ ] Terminal 1: API running
- [ ] Terminal 2: Client running  
- [ ] Terminal 3: Stripe CLI running
- [ ] WebhookSecret updated in Key Vault
- [ ] API restarted after WebhookSecret update
- [ ] Browser ready: http://localhost:5014

---

## 🌱 Test Data Seeding

### Overview

System automatically seeds **3 example rental companies** with complete data:
- ✅ Tenants (rental companies)
- ✅ CompanyInfo (with NIP, REGON, address)
- ✅ Products (equipment for rent)
- ✅ Customers (test clients)

---

### Seeded Tenants

#### **1. Wypożyczalnia 'Narty & Snowboard' Zakopane**
```
🎿 Specialization: Skis, snowboards
📍 Location: Zakopane
📊 Products: 6 (skis, snowboards, boots, helmets)
💰 Prices: 25-120 PLN/day
```

**CompanyInfo:**
- NIP: `7362614562`
- REGON: `012345678`
- Address: `ul. Krupówki 12/3, 34-500 Zakopane`
- Email: `kontakt@nartyzakopane.pl`
- Phone: `+48 18 201 50 00`

**Products:**
| Product | SKU | Price/day | Available |
|---------|-----|-----------|-----------|
| Narty Rossignol Hero Elite ST Ti | SKI-ROSS-001 | 120 PLN | 15 pcs |
| Narty Atomic Redster X9 | SKI-ATOM-002 | 110 PLN | 10 pcs |
| Snowboard Burton Custom | SNB-BURT-001 | 100 PLN | 8 pcs |
| Buty narciarskie Salomon | BOOT-SAL-001 | 40 PLN | 20 pcs |
| Kask Smith Vantage MIPS | HELM-SMI-001 | 25 PLN | 25 pcs |
| Gogle Oakley Flight Deck | GOGL-OAK-001 | 30 PLN | 15 pcs |

---

#### **2. BIKE RENTAL Kraków - Rowery Miejskie**
```
🚲 Specialization: City bikes, electric, MTB
📍 Location: Kraków (Main Square)
📊 Products: 5 (bikes, helmets, child seats)
💰 Prices: 15-120 PLN/day
```

**CompanyInfo:**
- NIP: `6762512345`
- REGON: `357890123`
- Address: `Rynek Główny 15, 31-008 Kraków`
- Email: `info@bikerental.krakow.pl`
- Phone: `+48 12 345 67 89`

**Products:**
| Product | SKU | Price/day | Available |
|---------|-----|-----------|-----------|
| Rower miejski Trek FX 3 | BIKE-TRK-001 | 60 PLN | 12 pcs |
| Rower elektryczny Giant | EBIKE-GNT-001 | 120 PLN | 8 pcs |
| Rower górski Scott Scale | MTB-SCT-001 | 80 PLN | 6 pcs |
| Kask rowerowy Specialized | HELM-SPC-001 | 15 PLN | 20 pcs |
| Fotelik dziecięcy Thule | SEAT-THU-001 | 20 PLN | 5 pcs |

---

#### **3. Surf & SUP Hel - Wypożyczalnia Sportów Wodnych**
```
🏄 Specialization: SUP, windsurfing, kitesurfing
📍 Location: Hel Peninsula
📊 Products: 5 (boards, wetsuits, vests)
💰 Prices: 20-150 PLN/day
```

**CompanyInfo:**
- NIP: `5882345678`
- REGON: `220345678`
- Address: `ul. Wiejska 72, 84-150 Hel`
- Email: `biuro@surfsup-hel.pl`
- Phone: `+48 58 675 12 34`

**Products:**
| Product | SKU | Price/day | Available |
|---------|-----|-----------|-----------|
| Deska SUP Red Paddle Co | SUP-RED-001 | 80 PLN | 10 pcs |
| Deska windsurf Fanatic | WIND-FAN-001 | 100 PLN | 6 pcs |
| Zestaw kitesurfing North | KITE-NOR-001 | 150 PLN | 4 pcs |
| Pianka neoprenowa ION | WET-ION-001 | 40 PLN | 15 pcs |
| Kamizelka Jobe | VEST-JOB-001 | 20 PLN | 20 pcs |

---

### Seeded Customers

Each tenant has the same 5 test customers:

| Name | Email | Phone | Document |
|------|-------|-------|----------|
| Jan Kowalski | jan.kowalski@example.com | +48 601 234 567 | ABC123456 |
| Anna Nowak | anna.nowak@example.com | +48 602 345 678 | DEF234567 |
| Piotr Wiśniewski | piotr.wisniewski@example.com | +48 603 456 789 | GHI345678 |
| Katarzyna Zielińska | katarzyna.zielinska@example.com | +48 604 567 890 | JKL456789 |
| Marek Dąbrowski | marek.dabrowski@example.com | +48 605 678 901 | MNO567890 |

---

### Data Statistics

| Metric | Count |
|--------|-------|
| **Tenants** | 3 |
| **CompanyInfos** | 3 |
| **Products** | 16 (6+5+5) |
| **Customers** | 15 (5 per tenant) |
| **Total Inventory** | 171 items |

---

### How to Seed Data

#### **Method 1: Automatic (on first run)**

Data is automatically seeded when you first run the application:

```bash
# 1. Apply migrations
dotnet ef database update --project SportRental.Admin

# 2. Run application (seeding happens automatically in Development)
dotnet run --project SportRental.Admin
```

**Output:**
```
🌱 Starting test data seeding...
  ✅ Created tenant: Wypożyczalnia 'Narty & Snowboard' Zakopane
     ✅ Created CompanyInfo with NIP: 7362614562
     ✅ Created 6 products
     ✅ Created 5 customers
🎉 Test data seeding completed!
```

---

#### **Method 2: PowerShell Script**

Use the ready-made script:

```powershell
./scripts/database/reset-and-seed-database.ps1
```

**What it does:**
1. Drops the database
2. Applies migrations
3. Runs application (auto-seeding)

---

## 🎯 Test Scenarios

### Scenario 1: Full Rental Flow (Zakopane)

```gherkin
GIVEN I am on tenant "Wypożyczalnia 'Narty & Snowboard' Zakopane"
  AND products are available
WHEN I select "Narty Rossignol Hero Elite ST Ti" (120 PLN/day)
  AND I choose customer "Jan Kowalski"
  AND I set rental dates: 3 days (08-11.10.2025)
  AND I proceed to checkout
  AND I pay deposit: 324 PLN (30%)
THEN Rental is created with status "Pending"
  AND Stripe payment succeeds
  AND Rental status changes to "Confirmed"
  AND Email is sent to jan.kowalski@example.com
  AND Email contains PDF contract
  AND PDF shows:
      • Company: Wypożyczalnia 'Narty & Snowboard' Zakopane
      • NIP: 7362614562
      • REGON: 012345678
      • Address: ul. Krupówki 12/3, 34-500 Zakopane
      • Product: Narty Rossignol Hero Elite ST Ti
      • Price: 1080 PLN (3 days × 120 PLN)
      • Deposit: 324 PLN
```

---

### Scenario 2: Multi-Product Rental (Kraków)

```gherkin
GIVEN I am on tenant "BIKE RENTAL Kraków"
WHEN I add to cart:
      • Rower miejski Trek (60 PLN/day)
      • Kask rowerowy (15 PLN/day)
      • Fotelik dziecięcy (20 PLN/day)
  AND I set rental: 2 days
  AND Customer: "Anna Nowak"
  AND I pay: 57 PLN (deposit)
THEN Total is: 190 PLN (2 × 95 PLN)
  AND PDF shows all 3 items
  AND Company info: NIP 6762512345, REGON 357890123
```

---

### Scenario 3: High-Value Rental (Hel)

```gherkin
GIVEN I am on tenant "Surf & SUP Hel"
WHEN I rent "Zestaw kitesurfing North Rebel" (150 PLN/day)
  AND Customer: "Piotr Wiśniewski"
  AND Duration: 5 days
  AND I pay: 225 PLN (deposit 30%)
THEN Total: 750 PLN
  AND PDF contract generated
  AND Company: Surf & SUP Hel, NIP 5882345678
```

---

### Scenario 4: Multi-Tenancy Isolation

```gherkin
GIVEN Database has 3 tenants seeded
WHEN I log in to "Zakopane" tenant
THEN I see only 6 products (narty, snowboardy)
  AND I see only 5 customers from Zakopane

WHEN I switch to "Kraków" tenant
THEN I see only 5 products (rowery)
  AND I see only 5 customers from Kraków
  AND I do NOT see Zakopane products
```

---

## 🐛 Troubleshooting

### Stripe Issues

**❌ "Cannot redirect to Stripe"**
- **Fix:** Check if API is running on http://localhost:5001
- **Fix:** Verify Stripe keys in Azure Key Vault

**❌ "Webhooks not working"**
- **Fix:** Check Terminal 3 - is Stripe CLI active?
- **Fix:** Copy webhook secret (whsec_...) to Key Vault
- **Fix:** Restart API after updating WebhookSecret

**❌ "CORS error"**
- **Fix:** Ensure Client runs on http://localhost:5014 (not another port!)

---

### Database Issues

**❌ "Database already contains data"**
```bash
# Solution: Reset database
./scripts/database/reset-and-seed-database.ps1
```

**❌ "test-data.json not found"**
```bash
# Check if file exists in root
ls test-data.json
```

**❌ "Seeding didn't run"**
```bash
# Check logs
dotnet run --project SportRental.Admin | grep "seeding"

# Verify environment
echo $env:ASPNETCORE_ENVIRONMENT  # Should be "Development"
```

**❌ "Products not showing in UI"**
```sql
-- Check database
SELECT * FROM "Products" WHERE "TenantId" = 'your-tenant-id';
SELECT * FROM "Tenants";
SELECT * FROM "CompanyInfos";
```

---

## 📚 Related Documentation

- [SECURITY.md](../SECURITY.md) - Azure Key Vault setup
- [setup/STRIPE_SANDBOX_GUIDE.md](setup/STRIPE_SANDBOX_GUIDE.md) - Detailed Stripe docs
- [setup/AZURE_KEY_VAULT_SETUP.md](setup/AZURE_KEY_VAULT_SETUP.md) - Secret management
- Stripe Dashboard: https://dashboard.stripe.com/test/payments
- Stripe Testing: https://stripe.com/docs/testing

---

**Last updated:** 2025-10-07  
**Status:** ✅ Production Ready  
**Mode:** Sandbox/Test 🧪
