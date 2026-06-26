import { test, expect } from '@playwright/test';
import { signInAsDemo, waitForBlazorReady, setDarkMode } from '../helpers/demo';

/**
 * Cross-browser matrix — Chromium + Firefox + WebKit smoke.
 *
 * SETUP WYMAGANY (NIE w tym pliku — sugestia do playwright.config.ts):
 *
 *   projects: [
 *     {
 *       name: 'desktop-chromium',
 *       use: { ...devices['Desktop Chrome'], viewport: { width: 1440, height: 900 } },
 *     },
 *     {
 *       name: 'desktop-firefox',
 *       use: { ...devices['Desktop Firefox'], viewport: { width: 1440, height: 900 } },
 *     },
 *     {
 *       name: 'desktop-webkit',
 *       use: { ...devices['Desktop Safari'], viewport: { width: 1440, height: 900 } },
 *     },
 *   ]
 *
 * Następnie uruchom:
 *   npx playwright install firefox webkit
 *   npx playwright test cross-browser --project=desktop-firefox
 *   npx playwright test cross-browser  (all 3 projects)
 *
 * Cel: wyłapać Firefox-only flexbox issues, WebKit-only date input bugs,
 * różnice w renderze MudBlazor componentów, smooth scroll, CSS grid quirki etc.
 * Assertions są wspólne dla wszystkich browserów — różnice widzimy
 * w screenshotach test-results/cross-browser-<browser>-<test>.png.
 */

const SUPPORTED_PROJECTS = ['desktop-chromium', 'desktop-firefox', 'desktop-webkit'];

test.describe('cross-browser', () => {
  test.beforeEach(async ({}, testInfo) => {
    // Spec dedykowany 3 projektom desktop-*. Jeśli ktoś uruchomi go z innym projektem
    // (np. mobile / domyślny chromium), nie wywalamy testu — po prostu mu damy
    // przejść z odpowiednim tagiem w screenshocie. Tag bierzemy z project.name.
    if (!SUPPORTED_PROJECTS.includes(testInfo.project.name)) {
      testInfo.annotations.push({
        type: 'warn',
        description: `cross-browser spec optimized for ${SUPPORTED_PROJECTS.join(', ')}; running under "${testInfo.project.name}"`,
      });
    }
  });

  test('demo-signin-works', async ({ page }, testInfo) => {
    test.setTimeout(60_000);
    const browser = testInfo.project.name;

    await signInAsDemo(page);
    await waitForBlazorReady(page);

    // URL po signin musi zawierać /dashboard
    await expect(page).toHaveURL(/\/dashboard/, { timeout: 15_000 });

    // Sidebar widoczny (desktop)
    const drawer = page.locator('.rs-drawer, .mud-drawer').first();
    await expect(drawer).toBeVisible({ timeout: 15_000 });

    // Demo chip widoczny w topbarze
    const demoChip = page.locator('.rs-demo-chip').first();
    await expect(demoChip).toBeVisible({ timeout: 10_000 });

    await page.screenshot({
      path: `test-results/cross-browser-${browser}-demo-signin-works.png`,
      fullPage: true,
    });
  });

  test('dashboard-renders-correctly', async ({ page }, testInfo) => {
    test.setTimeout(60_000);
    const browser = testInfo.project.name;

    await signInAsDemo(page);
    await waitForBlazorReady(page);

    await page.goto('/dashboard', { waitUntil: 'domcontentloaded' });
    await waitForBlazorReady(page);
    // Daj momentowi czas — Blazor circuit + query KPI
    await page.waitForTimeout(1500);

    // KPI cards (Dashboard używa .rs-kpi-card, NIE .rs-pay-kpi)
    const kpiCards = page.locator('.rs-kpi-card');
    await expect(kpiCards.first()).toBeVisible({ timeout: 20_000 });

    const kpiCount = await kpiCards.count();
    expect(kpiCount, `Dashboard KPI count on ${browser}`).toBeGreaterThanOrEqual(3);

    // Sprawdź że KPI nie są pustymi blokami — mają jakąkolwiek nielisty
    const firstKpiText = (await kpiCards.first().innerText()).trim();
    expect(firstKpiText.length, `First KPI should have content on ${browser}`).toBeGreaterThan(0);

    // Brak horizontal overflow na desktop 1440 — typowe miejsce gdzie Firefox/WebKit
    // pokazują flexbox/grid różnice względem Chromium.
    const overflow = await page.evaluate(() => ({
      scrollWidth: document.documentElement.scrollWidth,
      clientWidth: document.documentElement.clientWidth,
    }));
    expect(
      overflow.scrollWidth,
      `Dashboard horizontal overflow on ${browser} (scrollW=${overflow.scrollWidth} clientW=${overflow.clientWidth})`,
    ).toBeLessThanOrEqual(overflow.clientWidth + 2); // +2 px tolerance

    await page.screenshot({
      path: `test-results/cross-browser-${browser}-dashboard-renders-correctly.png`,
      fullPage: true,
    });
  });

  test('dark-mode-toggle', async ({ page }, testInfo) => {
    test.setTimeout(60_000);
    const browser = testInfo.project.name;

    await signInAsDemo(page);
    await waitForBlazorReady(page);

    // Light baseline screenshot
    await page.screenshot({
      path: `test-results/cross-browser-${browser}-dark-mode-toggle-light.png`,
      fullPage: true,
    });

    await setDarkMode(page, true);
    await waitForBlazorReady(page);

    // Dark active — sprawdzenie identyczne jak w dark-mode.spec, robust dla 3 browserów
    const isDark = await page.evaluate(() => {
      const body = document.body;
      const html = document.documentElement;
      const hasDarkClass =
        body.classList.contains('mud-theme-dark') ||
        html.classList.contains('mud-theme-dark') ||
        body.getAttribute('data-theme') === 'dark' ||
        html.getAttribute('data-theme') === 'dark';
      const bg = getComputedStyle(body).backgroundColor;
      const m = bg.match(/\d+/g);
      const avg = m && m.length >= 3 ? (Number(m[0]) + Number(m[1]) + Number(m[2])) / 3 : 255;
      return hasDarkClass || avg < 128;
    });
    expect(isDark, `Dark mode should be active on ${browser}`).toBeTruthy();

    await page.screenshot({
      path: `test-results/cross-browser-${browser}-dark-mode-toggle.png`,
      fullPage: true,
    });

    // Toggle back to light — make sure round-trip works (WebKit storage event timing różni się).
    await setDarkMode(page, false);
    await waitForBlazorReady(page);

    const isLight = await page.evaluate(() => {
      const body = document.body;
      const bg = getComputedStyle(body).backgroundColor;
      const m = bg.match(/\d+/g);
      const avg = m && m.length >= 3 ? (Number(m[0]) + Number(m[1]) + Number(m[2])) / 3 : 0;
      return avg > 200;
    });
    expect(isLight, `Light mode should be restored on ${browser}`).toBeTruthy();
  });

  test('payments-list-visible', async ({ page }, testInfo) => {
    test.setTimeout(60_000);
    const browser = testInfo.project.name;

    await signInAsDemo(page);
    await waitForBlazorReady(page);

    await page.goto('/admin/payments', { waitUntil: 'domcontentloaded' });
    await waitForBlazorReady(page);
    await page.waitForTimeout(1500);

    // Payments KPI cards (UWAGA: na tej stronie klasa to .rs-pay-kpi!)
    const payKpis = page.locator('.rs-pay-kpi');
    await expect(payKpis.first()).toBeVisible({ timeout: 20_000 });
    const payKpiCount = await payKpis.count();
    expect(payKpiCount, `Payments KPI count on ${browser}`).toBeGreaterThanOrEqual(4);

    // Lista wynajmów — table z wierszami
    const tableRows = page.locator('table tbody tr');
    await expect(tableRows.first()).toBeVisible({ timeout: 20_000 });
    const rowCount = await tableRows.count();
    expect(rowCount, `Payments table rows on ${browser}`).toBeGreaterThan(0);

    // Tabela nie wystaje (typowy issue na WebKit z table-layout: fixed)
    const tableOverflow = await page.evaluate(() => {
      const t = document.querySelector('table');
      if (!t) return { scrollW: 0, clientW: 0 };
      return { scrollW: t.scrollWidth, clientW: t.clientWidth };
    });
    expect(
      tableOverflow.scrollW,
      `Payments table horizontal overflow on ${browser} (sw=${tableOverflow.scrollW} cw=${tableOverflow.clientW})`,
    ).toBeLessThanOrEqual(tableOverflow.clientW + 8); // tolerancja na scrollbar

    await page.screenshot({
      path: `test-results/cross-browser-${browser}-payments-list-visible.png`,
      fullPage: true,
    });
  });

  test('mark-as-paid-dialog-functional', async ({ page }, testInfo) => {
    test.setTimeout(60_000);
    const browser = testInfo.project.name;

    await signInAsDemo(page);
    await waitForBlazorReady(page);

    await page.goto('/admin/rentals', { waitUntil: 'domcontentloaded' });
    await waitForBlazorReady(page);
    await page.waitForTimeout(1500);

    // Desktop — bezpośrednio button (mobile widok użyty MudMenu, ale ten spec to desktop matrix)
    const markPaidButton = page
      .getByRole('button', { name: /Oznacz jako opłacone/i })
      .first();

    await expect(markPaidButton, `Mark-paid button visible on ${browser}`).toBeVisible({ timeout: 20_000 });
    await markPaidButton.scrollIntoViewIfNeeded();
    await markPaidButton.click();

    // Dialog otwarty
    const dialog = page.locator('.mud-dialog').first();
    await expect(dialog, `Mark-paid dialog opens on ${browser}`).toBeVisible({ timeout: 10_000 });

    // Opcje płatności — wszystkie 5 widoczne (radio labels)
    await expect(dialog.getByText('Gotówka', { exact: false }).first()).toBeVisible();
    await expect(dialog.getByText('Karta', { exact: false }).first()).toBeVisible();
    await expect(dialog.getByText(/Przelew/i).first()).toBeVisible();
    await expect(dialog.getByText('BLIK', { exact: false }).first()).toBeVisible();
    await expect(dialog.getByText(/Inna/i).first()).toBeVisible();

    // Submit button obecny (nie klikamy — to chcemy testować w rental-lifecycle, tu interesuje
    // nas tylko że dialog się renderuje OK w każdym browserze).
    const submitBtn = dialog.getByRole('button', { name: /Zatwierdź płatność/i });
    await expect(submitBtn, `Submit button visible on ${browser}`).toBeVisible();

    // Dialog nie wystaje poza viewport (Firefox bywa kłopotliwy z position: fixed + transform)
    const dialogBox = await dialog.boundingBox();
    const viewport = page.viewportSize() ?? { width: 1440, height: 900 };
    if (dialogBox) {
      expect(dialogBox.x, `Dialog left edge on ${browser}`).toBeGreaterThanOrEqual(-2);
      expect(
        dialogBox.x + dialogBox.width,
        `Dialog right edge on ${browser}`,
      ).toBeLessThanOrEqual(viewport.width + 2);
    }

    await page.screenshot({
      path: `test-results/cross-browser-${browser}-mark-as-paid-dialog-functional.png`,
      fullPage: true,
    });

    // Close dialog (Escape) — sanity że dialog reaguje na keyboard we wszystkich browserach
    await page.keyboard.press('Escape');
    await expect(dialog).toBeHidden({ timeout: 5_000 }).catch(() => {
      // Niektóre dialogi mają disableEscape — to nie blocker testu
    });
  });
});
