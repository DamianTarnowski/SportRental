# AGENTS.md — SportRental

Single source of truth for AI coding agents (Claude Code, Codex, Cursor, Copilot, Windsurf, …).
**Read this before making assumptions about the stack, architecture, or conventions** — docs elsewhere
in the repo have drifted in the past; this file is kept in sync with the code. The repo is **public**,
so this file contains no secrets or private infrastructure names.

## What this is
Multi-tenant SaaS for **sport-equipment rental businesses** (PL market). One deployment serves many
tenants (rental shops); each tenant is isolated via EF Core global query filters on `TenantId`.

## Tech stack (verified against the csproj files — keep exact)
- **Runtime:** .NET 10 (`net10.0`), C# 14 (net10.0 default — `LangVersion` is not pinned).
- **Frontend:** Blazor **Server** (Admin panel) + Blazor **WebAssembly** (public Client). MudBlazor
  (`8.15.0` in Admin/Client, `8.13.0` in Shared — known skew) + TailwindCSS (Client). SignalR.
- **Data:** **EF Core `9.0.9`** (NOT v10) + **Npgsql provider `9.0.4`** → **PostgreSQL**. Some
  ASP.NET Core packages are on 10.x while EF stays on 9.x — this mix is intentional. No central package
  management (versions are per-csproj), no `Directory.Build.props`, no `global.json`.
- **Integrations:** Stripe.net `49.0.0` (Checkout Sessions + PaymentIntents), QuestPDF `2025.7.2`
  (contracts/invoices), SMSAPI.pl `2.2.1` (SMS), MailKit (SMTP email), Azure.Identity + Azure Blob +
  Key Vault config provider, BarcodeLib/QRCoder + SkiaSharp (Code 128 barcodes).
- **Tests:** xUnit + bUnit + Moq + FluentAssertions + Testcontainers.PostgreSql; E2E is NUnit + Playwright.
- **SQLite** is used **only** by the standalone `SportRental.MediaStorage` microservice — the main app is PostgreSQL.

## Solution layout & architecture reality (this trips up agents)
`SportRentalHybrid.sln` contains **9 projects**: `SportRental.Admin`, `.Admin.Tests`, `.Client`,
`.Client.Tests`, `.Shared`, `.Infrastructure`, `.MediaStorage`, `.MediaStorage.Tests`, `.Backend`.

- **`SportRental.Admin`** (Blazor Server, AssemblyName `SportRental`) is the whole app: it renders the
  admin UI, **hosts the REST API in-process** (`MapSportRentalApi` / `MapControllers` + JWT bearer
  alongside Identity cookies), **and serves the Client WASM bundled under `/_client/`** via the
  `PublishClientWasmIntoAdmin` MSBuild target. There is **no separate API service running**.
- **`SportRental.Api/`** is an **empty placeholder folder** (no code, not in the sln). The former
  standalone API lives in **`_DEPRECATED_SportRental.Api/`** (full code, excluded from the build).
  Do not treat either as a live/production API.
- **`SportRental.MediaStorage`** is **idle/optional** — Admin does not reference it; files go straight to
  Azure Blob (`Storage:Provider=AzureBlob`). It only runs as a dev-only auto-start service.
- **`SportRental.Infrastructure`** = EF Core `ApplicationDbContext`, domain entities, migrations.
- **`SportRental.Backend`** = build/test helper that link-compiles `SportRental.Admin/Data/**/*.cs`
  (shared EF layer) so tooling/tests can use it without the full web host.
- Loose folders `DbTool/`, `DbQuery/`, `TempHasher/`, `SportRental.E2ETests/`, and `Sport Rental old project/`
  are **not** in the sln — utilities/legacy, don't build them as part of the solution.

## Build · run · test
```bash
dotnet build SportRentalHybrid.sln                                   # solution build (0 errors expected)
dotnet run --project SportRental.Admin --urls http://localhost:5001  # admin UI + REST API (+ Swagger at /swagger in Dev)
dotnet run --project SportRental.Client --urls http://localhost:5014 # standalone Client dev (prod uses the bundled /_client/)
dotnet test SportRental.Admin.Tests        # ~303 xUnit tests
dotnet test SportRental.Client.Tests       # 6 tests
dotnet test SportRental.MediaStorage.Tests # 6 tests
```
- The 3 in-sln xUnit projects total **~315 tests**. `SportRental.E2ETests` (NUnit + Playwright, ~52 tests)
  is **outside the sln** and needs a live app — run it separately.
- **Environment for tests/dev (never hardcode these):**
  - `SR_TEST_DB` — connection string for integration tests (they hit a real Postgres).
  - `SR_TEST_GMAIL_APP_PASSWORD` — Gmail app-password for the email E2E test.
  - `Admin:DevPassword` (user-secrets/env) — dev superadmin seed password; without it the seed is skipped.

## Conventions & HARD RULES
- **Secrets:** the repo is **public**. **Never** put secrets, tokens, passwords, or connection strings in
  code, tests, `appsettings*.json`, or docs. All secrets come from **Azure Key Vault** via
  `DefaultAzureCredential` (the config provider maps `--` → `:`, e.g. `Email--Smtp--Password`). Locally
  use user-secrets / environment variables.
- **No CI / no GitHub Actions.** Build, test, and deploy are done **locally**. Do not add
  `.github/workflows`, `act`, `husky`, or similar — this is a deliberate choice.
- **No Docker by default.** Use local service installs (or Azure managed services); do not add
  Dockerfiles / docker-compose / Testcontainers-for-everything without asking.
- **QR codes are disabled in the UI** (security, since 2026-04) and replaced by **Code 128 barcodes**
  (`BarcodeGenerator`). QR generators still exist in code but are unused — don't reintroduce QR in the UI.
- **Demo tenants** (`Tenant.IsDemo`) must **never send real SMS/email**. `DemoAware{Sms,Email}Sender`
  suppress-and-log in request scope; the background `RentalReminderService` explicitly skips demo tenants.
- **AI write-actions** (if/when added) must be **configurable per tenant** (Suggest vs AutoExecute) with an
  audit log — do not force a confirmation gate on every operation.
- **Language:** the maintainer communicates in **Polish** — write commit messages and user-facing prose in
  Polish; keep code, identifiers, and this file in English.

## Deployment (model only — specific resource names are in the maintainer's private notes)
- Hosted on **Azure App Service (Linux)**. Deploy is **manual** (Visual Studio Publish / `az webapp deploy`)
  from local `main` — local `main` is the source of truth. Publish must be `--runtime linux-x64 --self-contained false`.
- Secrets via **Azure Key Vault** + `DefaultAzureCredential`; **PostgreSQL** (Azure Database for PostgreSQL,
  a shared server); EF migrations run at startup. DataProtection keys persist under `/home/data`.
- **Legacy (do not target):** the older Windows `sradmin` App Service and the Azure Static Web Apps /
  `srclient-blazor` client hosting are deprecated. Current prod is the Linux App Service above.

## Notifications
- **Email:** MailKit SMTP, active only when `Email:Smtp:Enabled=true` (otherwise a silent `NoOpEmailSender`).
- **SMS:** routed by `SmsSenderRouter` on `Sms:Provider` → SMSAPI.pl / SerwerSMS / Console. Disabled sends
  log `[SMS-DISABLED]`.
- **Reminders:** `RentalReminderService` (5-min timer) — daily rentals get a 24h-before reminder, hourly a
  30-min-before; a **final 30-min-before-return reminder** goes out by **email + SMS**. Demo tenants skipped.

## Common gotchas
- EF Core on .NET 9/10: `new[]{ … }.Contains(x)` inside a `Where` throws `TypeLoadException` — use an OR
  chain or a `List<>` instead.
- PostgreSQL: `sr_user` may not own tables; `ALTER TABLE` migrations can fail — fix once with
  `REASSIGN OWNED BY pgadmin TO sr_user`.
- MudBlazor version skew between `Shared` (8.13) and Admin/Client (8.15) — align before upgrading.

## Where to look next
`README.md` (overview) · `doc/ARCHITECTURE.md` · `doc/DEVELOPER_GUIDE.md` · `doc/API_DOCUMENTATION.md` ·
`doc/TESTING_GUIDE.md` · `CASE_STUDY.md` · `doc/setup/*` (Key Vault, Blob, SMTP, Stripe).
