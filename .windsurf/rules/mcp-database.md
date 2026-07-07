---
trigger: always
---

# MCP Database — SportRentalHybrid

Projekt używa PostgreSQL (baza `sr`) na Azure. Połączenie i sekrety idą przez **Azure Key Vault /
konfigurację** — repo jest **publiczne**, więc NIE hardkoduj connection stringów, hostów ani haseł
w kodzie/testach/docach (lokalnie: user-secrets / zmienne środowiskowe).

Do zapytań do bazy używaj skonfigurowanego serwera MCP postgres tego projektu (szukaj toola z prefiksem
`postgres-sr`). Nie używaj innych serwerów postgres (electric, procviewer, toolkit, am, voicebotdemo)
chyba że user jawnie poprosi.

Pełny kontekst projektu: patrz **AGENTS.md** w rootcie repo.
