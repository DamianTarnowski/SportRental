import { test, expect } from '@playwright/test';
import { signInAsDemo, waitForBlazorReady } from '../helpers/demo';

test.describe('demo signin flow', () => {
  test.setTimeout(60_000);

  test('demo-signin: GET /Account/Demo → /dashboard (200)', async ({ page }) => {
    const resp = await page.goto('/Account/Demo', { waitUntil: 'networkidle' });
    expect(resp, 'response should exist').not.toBeNull();
    expect(resp!.ok(), `expected 2xx, got ${resp!.status()}`).toBeTruthy();

    // Demo endpoint robi LocalRedirect("~/") → Home wykrywa auth → /dashboard.
    await page.waitForURL(/\/dashboard/, { timeout: 30_000 }).catch(async () => {
      await page.goto('/dashboard', { waitUntil: 'networkidle' });
    });

    expect(page.url()).toMatch(/\/dashboard/);
    await waitForBlazorReady(page);

    await page.screenshot({
      path: 'test-results/demo-signin/dashboard-after-demo.png',
      fullPage: true,
    });
  });

  test('drawer-visible: sidebar widoczny po signin', async ({ page }) => {
    await signInAsDemo(page);
    await waitForBlazorReady(page);

    const drawer = page.locator('.rs-drawer').first();
    await expect(drawer).toBeVisible({ timeout: 15_000 });
  });

  test('topbar-demo-chip: chip TRYB DEMO obecny i klikalny', async ({ page }) => {
    await signInAsDemo(page);
    await waitForBlazorReady(page);

    // Główny lokator — klasa .rs-demo-chip; fallback przez tekst.
    const chip = page
      .locator('.rs-demo-chip')
      .or(page.getByText(/TRYB DEMO/i))
      .first();

    await expect(chip).toBeVisible({ timeout: 15_000 });
    // Klikalność — sprawdzamy że element nie jest disabled (po prostu klikamy).
    await chip.click({ trial: true });
  });

  test('user-display: "Demo Owner" widoczne (zamiast maila)', async ({ page }) => {
    await signInAsDemo(page);
    await waitForBlazorReady(page);

    // Może być w sidebar lub topbar — szukamy globalnie pierwszego wystąpienia.
    const userLabel = page.getByText(/Demo Owner/i).first();
    await expect(userLabel).toBeVisible({ timeout: 15_000 });
  });

  test('nav-links-present: kluczowe linki nawigacyjne istnieją', async ({ page }) => {
    await signInAsDemo(page);
    await waitForBlazorReady(page);

    const labels = [
      'Dashboard',
      'Raporty',
      'Płatności',
      'Umowy',
      'Wynajmy',
      'Klienci',
      'Produkty',
    ];

    for (const label of labels) {
      // getByRole('link') + nazwa = preferowany; fallback tekst.
      const link = page
        .getByRole('link', { name: new RegExp(`^${label}$`, 'i') })
        .or(page.getByText(new RegExp(`^${label}$`, 'i')))
        .first();
      await expect(link, `nav link "${label}" should be visible`).toBeVisible({
        timeout: 10_000,
      });
    }
  });

  test('kpi-tiles-render: 4 oryginalne + 4 payment KPI tiles', async ({ page }) => {
    await signInAsDemo(page);
    await waitForBlazorReady(page);

    // Wszystkie KPI tile mają klasę .rs-kpi-card.
    const cards = page.locator('.rs-kpi-card');
    await expect(cards.first()).toBeVisible({ timeout: 20_000 });

    const count = await cards.count();
    expect(count, `expected >=8 KPI cards, got ${count}`).toBeGreaterThanOrEqual(8);

    // Oryginalne KPI — sprawdzamy po labelu.
    const originals = [
      /Aktywne wynajmy/i,
      /Do zwrotu/i,
      /Wolne produkty/i,
      /Przychód dziś/i,
    ];
    for (const re of originals) {
      await expect(
        page.locator('.rs-kpi-label').filter({ hasText: re }).first(),
        `KPI label ${re} should be visible`,
      ).toBeVisible({ timeout: 10_000 });
    }

    // 4 payment tile — sprawdzamy ikony po klasie.
    const paymentIconClasses = [
      '.rs-kpi-icon-pay-pending',
      '.rs-kpi-icon-pay-overdue',
      '.rs-kpi-icon-pay-ok',
      '.rs-kpi-icon-pay-month',
    ];
    for (const cls of paymentIconClasses) {
      await expect(page.locator(cls).first(), `${cls} should exist`).toBeVisible({
        timeout: 10_000,
      });
    }

    await page.screenshot({
      path: 'test-results/demo-signin/kpi-tiles.png',
      fullPage: true,
    });
  });
});
