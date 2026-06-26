import type { Page } from '@playwright/test';

/**
 * Demo signin — wywołuje /Account/Demo (GET endpoint) który tworzy świeży tenant + sign-in cookie,
 * potem redirect przez Home → /dashboard.
 *
 * Po wywołaniu page jest auth'd. Tenant ma 8h TTL (DemoTenantSeeder).
 */
export async function signInAsDemo(page: Page): Promise<void> {
  // UWAGA: Blazor Server trzyma persistent WebSocket /_blazor → 'networkidle' NIGDY nie zachodzi.
  // Używamy 'domcontentloaded' (DOM gotowy) i jawnie czekamy na URL /dashboard.
  const resp = await page.goto('/Account/Demo', { waitUntil: 'domcontentloaded', timeout: 30_000 });
  if (!resp || !resp.ok()) {
    throw new Error(`Demo signin failed: ${resp?.status()}`);
  }
  await page.waitForURL(/\/dashboard/, { timeout: 30_000 }).catch(async () => {
    await page.goto('/dashboard', { waitUntil: 'domcontentloaded' });
  });
}

/**
 * Czeka aż Blazor interactive boot — drawer (sidebar) staje się widoczny na desktop.
 * Na mobile drawer jest schowany pod hamburger button.
 */
export async function waitForBlazorReady(page: Page): Promise<void> {
  // MudAppBar zawsze obecny po SSR, ale interaktywne elementy potrzebują circuit.
  await page.locator('.rs-appbar').waitFor({ state: 'visible', timeout: 20_000 });
  // Daj circuit moment na rehydrate
  await page.waitForTimeout(500);
}

/**
 * Wymusza tryb ciemny przez localStorage (per-device per memory ThemeService).
 * Po reload czeka aż inline-head-script ustawił data-theme=dark + .mud-theme-dark.
 */
export async function setDarkMode(page: Page, dark: boolean): Promise<void> {
  await page.evaluate((v) => localStorage.setItem('rs-theme-dark', v ? '1' : '0'), dark);
  await page.reload({ waitUntil: 'networkidle' });
  if (dark) {
    await page.waitForFunction(
      () => document.documentElement.classList.contains('mud-theme-dark')
         || document.documentElement.getAttribute('data-theme') === 'dark',
      { timeout: 10_000 }
    ).catch(() => { /* fall through — testy zdiagnozują */ });
  }
}
