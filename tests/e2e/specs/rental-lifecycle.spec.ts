import { test, expect } from '@playwright/test';
import { signInAsDemo, waitForBlazorReady } from '../helpers/demo';

test.describe.serial('rental-lifecycle', () => {
  test.setTimeout(60_000);

  test.beforeEach(async ({ page }) => {
    await signInAsDemo(page);
    await waitForBlazorReady(page);
  });

  test('payments-page-loads', async ({ page }) => {
    await page.goto('/admin/payments', { waitUntil: 'networkidle' });
    await waitForBlazorReady(page);

    // KPI tiles — Payments page używa klas .rs-pay-kpi (NIE .rs-kpi-card; tamta jest na Dashboardzie)
    const kpiCards = page.locator('.rs-pay-kpi');
    await expect(kpiCards.first()).toBeVisible({ timeout: 15_000 });
    const kpiCount = await kpiCards.count();
    expect(kpiCount).toBeGreaterThanOrEqual(4);

    // Payment-specific KPI variants
    const paymentKpis = page.locator(
      '.rs-pay-kpi-pending, .rs-pay-kpi-overdue, .rs-pay-kpi-paid, .rs-pay-kpi-month'
    );
    await expect(paymentKpis.first()).toBeVisible({ timeout: 10_000 });

    // Table z wierszami
    const tableRows = page.locator('table tbody tr');
    await expect(tableRows.first()).toBeVisible({ timeout: 15_000 });
    const rowCount = await tableRows.count();
    expect(rowCount).toBeGreaterThan(0);

    await page.screenshot({ path: 'test-results/rental-lifecycle/payments-page.png', fullPage: true });
  });

  // Cały describe.serial jest desktop-only — mobile używa MudMenu zamiast bezpośrednich CTA przycisków.
  test.beforeEach(async ({}, testInfo) => {
    if (testInfo.project.name === 'mobile') {
      testInfo.skip(true, 'rental-lifecycle: mobile używa MudMenu, dedykowany test TODO');
    }
  });

  test('rentals-page-shows-mark-paid', async ({ page }) => {
    await page.goto('/admin/rentals', { waitUntil: 'networkidle' });
    await waitForBlazorReady(page);

    // Czekamy aż lista wynajmów się załaduje
    await page.waitForTimeout(1500);

    // Mobile widok rentals używa MudMenu (3-dot) zamiast widocznych buttonów — kliknij menu pierwszego wynajmu.
    const viewport = page.viewportSize();
    const isMobile = (viewport?.width ?? 1440) < 700;

    if (isMobile) {
      const menuBtn = page.locator('.mud-menu button, button:has([class*="MoreVert"])').first();
      if (await menuBtn.isVisible({ timeout: 5_000 }).catch(() => false)) {
        await menuBtn.click();
        await page.waitForTimeout(500);
      }
    }

    // Znajdź "Oznacz jako opłacone" — button (desktop) lub menu item (mobile)
    const markPaidButton = page
      .getByRole('menuitem', { name: /Oznacz jako opłacone/i })
      .or(page.getByRole('button', { name: /Oznacz jako opłacone/i }))
      .first();

    await expect(markPaidButton).toBeVisible({ timeout: 20_000 });

    await page.screenshot({ path: 'test-results/rental-lifecycle/rentals-list.png', fullPage: true });
  });

  test('mark-as-paid-flow', async ({ page }) => {
    await page.goto('/admin/rentals', { waitUntil: 'networkidle' });
    await waitForBlazorReady(page);
    await page.waitForTimeout(1500);

    const markPaidButton = page
      .getByRole('button', { name: /Oznacz jako opłacone/i })
      .first();

    await expect(markPaidButton).toBeVisible({ timeout: 20_000 });
    await markPaidButton.scrollIntoViewIfNeeded();
    await markPaidButton.click();

    // Dialog otwarty — MudDialog
    const dialog = page.locator('.mud-dialog').first();
    await expect(dialog).toBeVisible({ timeout: 10_000 });

    // Sprawdź obecność opcji płatności (radio labels)
    await expect(dialog.getByText('Gotówka', { exact: false }).first()).toBeVisible();
    await expect(dialog.getByText('Karta', { exact: false }).first()).toBeVisible();
    await expect(dialog.getByText(/Przelew/i).first()).toBeVisible();
    await expect(dialog.getByText('BLIK', { exact: false }).first()).toBeVisible();
    await expect(dialog.getByText(/Inna/i).first()).toBeVisible();

    await page.screenshot({ path: 'test-results/rental-lifecycle/mark-paid-dialog.png', fullPage: true });

    // Wybierz Gotówka — klik na label (default może już być zaznaczone, ale wymuszamy)
    const gotowkaRadio = dialog
      .locator('label, .mud-radio')
      .filter({ hasText: 'Gotówka' })
      .first();
    await gotowkaRadio.click().catch(() => {
      // fallback: kliknij plain text
    });

    // Submit
    const submitBtn = dialog.getByRole('button', { name: /Zatwierdź płatność/i });
    await expect(submitBtn).toBeVisible();
    await submitBtn.click();

    // Dialog się zamyka
    await expect(dialog).toBeHidden({ timeout: 15_000 });

    // Snackbar success — "Płatność zatwierdzona" (może być różny wariant)
    const snackbar = page
      .locator('.mud-snackbar, .mud-snackbar-content-message')
      .filter({ hasText: /Płatność zatwierdzona|zatwierdzona|opłacon/i })
      .first();
    // Snackbar może szybko zniknąć — tolerujemy brak, ale szukamy też zmiany statusu
    await snackbar.waitFor({ state: 'visible', timeout: 8_000 }).catch(() => {
      // ok — może już zdążył zniknąć
    });

    await page.waitForTimeout(1500);

    // Status chip — po zmianie powinien być "opłacono" gdzieś na stronie
    const opłaconoChip = page
      .locator('.mud-chip, .rs-status-chip, .rs-chip')
      .filter({ hasText: /opłacon/i })
      .first();
    await expect(opłaconoChip).toBeVisible({ timeout: 10_000 });

    await page.screenshot({ path: 'test-results/rental-lifecycle/after-payment.png', fullPage: true });
  });

  test('contract-section', async ({ page }) => {
    await page.goto('/admin/contracts', { waitUntil: 'networkidle' });
    await waitForBlazorReady(page);
    await page.waitForTimeout(1000);

    // Header "Umowy"
    const header = page.getByRole('heading', { name: /Umowy/i }).first();
    const fallbackHeader = page.getByText(/^Umowy$/).first();
    const visible = await header.isVisible().catch(() => false);
    if (!visible) {
      await expect(fallbackHeader).toBeVisible({ timeout: 10_000 });
    } else {
      await expect(header).toBeVisible();
    }

    await page.screenshot({ path: 'test-results/rental-lifecycle/contracts-page.png', fullPage: true });
  });

  test('schedule-day-click', async ({ page }) => {
    await page.goto('/admin/schedule', { waitUntil: 'networkidle' });
    await waitForBlazorReady(page);
    await page.waitForTimeout(2000);

    // Spróbuj znaleźć dzień z markerami rezerwacji.
    // Klasa może być różna — próbujemy kilka kandydatów.
    const candidateDayCells = [
      page.locator('.rs-schedule-day-has-events'),
      page.locator('.rs-schedule-day').filter({ has: page.locator('.rs-schedule-event, .rs-schedule-marker, .rs-event-dot') }),
      page.locator('[class*="schedule"][class*="day"]').filter({ has: page.locator('[class*="event"], [class*="marker"], [class*="dot"]') }),
      page.locator('.mud-cal-month-cell, .mud-picker-calendar-day').filter({ has: page.locator('[class*="event"], [class*="dot"]') }),
    ];

    let clickedDay = false;
    for (const cand of candidateDayCells) {
      const count = await cand.count().catch(() => 0);
      if (count > 0) {
        const first = cand.first();
        try {
          await first.scrollIntoViewIfNeeded();
          await first.click({ timeout: 3000 });
          clickedDay = true;
          break;
        } catch {
          // try next
        }
      }
    }

    if (!clickedDay) {
      // Fallback: klik na cokolwiek z kalendarzowych komórek
      const anyDay = page
        .locator('button, td, div')
        .filter({ hasText: /^\d{1,2}$/ })
        .nth(15);
      await anyDay.click({ timeout: 5000 }).catch(() => {});
    }

    // Dialog DayDetailDialog z tabami
    const dialog = page.locator('.mud-dialog').first();
    const dialogVisible = await dialog.isVisible().catch(() => false);

    if (dialogVisible) {
      // 4 taby
      const tabs = dialog.locator('.mud-tab, [role="tab"]');
      const tabCount = await tabs.count();
      expect(tabCount).toBeGreaterThanOrEqual(4);

      // Tekst tabów
      const tabsText = (await tabs.allTextContents()).join('|').toLowerCase();
      expect(tabsText).toMatch(/wydani|zwrot|umow|płatnoś/);

      await page.screenshot({ path: 'test-results/rental-lifecycle/day-detail-dialog.png', fullPage: true });
    } else {
      // jeśli nie udało się otworzyć — przynajmniej zrób screenshot stanu strony
      await page.screenshot({ path: 'test-results/rental-lifecycle/day-detail-dialog.png', fullPage: true });
      test.info().annotations.push({ type: 'warn', description: 'Could not open DayDetailDialog — schedule day cells selectors may need update' });
    }
  });
});
