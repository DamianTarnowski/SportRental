import { test, expect, type Page } from '@playwright/test';
import { signInAsDemo, waitForBlazorReady, setDarkMode } from '../helpers/demo';

/**
 * Visual regression — baseline screenshots dla kluczowych UI.
 *
 * Pierwszy run zapisuje baseline w __snapshots__/<spec>/. Kolejne porównują pixel-by-pixel
 * z tolerancją maxDiffPixelRatio: 0.02 (2% pikseli). Animacje wyłączone.
 *
 * Aktualizacja baseline: `pnpm exec playwright test visual-regression --update-snapshots`.
 *
 * UWAGA: testy oznaczone "mobile-*" wymuszają project=mobile (iPhone 14). Pozostałe są
 * desktop-only (1440x900). Reszta jest skipowana per project — chcemy oddzielne baseliny
 * dla mobile i desktop.
 */

const SCREENSHOT_OPTS = {
  animations: 'disabled' as const,
  fullPage: true,
  maxDiffPixelRatio: 0.02,
  // Mask elementy zmienne (czas/data dynamic, snackbars) jeśli pojawią się w przyszłości.
  // Lista pusta na start — baseline złapie aktualny stan.
};

/**
 * Zatrzymuje wszelkie zegary / animacje, wyłącza caret blink, ukrywa elementy z czasem
 * (rs-demo-chip pokazuje pozostałe TTL, niedeterministyczne).
 */
async function freezeUi(page: Page): Promise<void> {
  await page.addStyleTag({
    content: `
      *, *::before, *::after {
        animation-duration: 0s !important;
        animation-delay: 0s !important;
        transition-duration: 0s !important;
        transition-delay: 0s !important;
        caret-color: transparent !important;
      }
      /* Demo chip pokazuje pozostały czas TTL — ukryj żeby nie psuł baseline */
      .rs-demo-chip { visibility: hidden !important; }
      /* MudBlazor snackbary mogą zostawać po nawigacji */
      .mud-snackbar-container { display: none !important; }
    `,
  });
  // Zatrzymaj wszystkie pending requestAnimationFrame (drobne re-rendery).
  await page.evaluate(() => {
    const root = document.documentElement;
    root.scrollTo({ top: 0, behavior: 'auto' });
  });
}

async function gotoAndSettle(page: Page, url: string, extraWaitMs = 1200): Promise<void> {
  await page.goto(url, { waitUntil: 'domcontentloaded' });
  await waitForBlazorReady(page);
  // Daj komponentom MudBlazor moment na late paint (queries, async loaders)
  await page.waitForTimeout(extraWaitMs);
  await freezeUi(page);
}

/** Klika pierwszy widoczny przycisk pasujący do dowolnego z patternów. */
async function tryClickFirst(
  page: Page,
  patterns: RegExp[],
  timeoutEach = 1500,
): Promise<boolean> {
  for (const pattern of patterns) {
    const candidates = [
      page.getByRole('button', { name: pattern }),
      page.getByRole('link', { name: pattern }),
      page.getByRole('menuitem', { name: pattern }),
    ];
    for (const cand of candidates) {
      const first = cand.first();
      try {
        if (await first.isVisible({ timeout: timeoutEach }).catch(() => false)) {
          await first.scrollIntoViewIfNeeded().catch(() => {});
          await first.click({ timeout: 3000 });
          return true;
        }
      } catch {
        // try next
      }
    }
  }
  return false;
}

test.describe('Visual regression — desktop baselines', () => {
  test.setTimeout(60_000);

  test.beforeEach(async ({ page }, testInfo) => {
    if (testInfo.project.name !== 'desktop') {
      testInfo.skip(true, 'Desktop baseline — uruchamiaj tylko na project=desktop');
    }
    await signInAsDemo(page);
    await waitForBlazorReady(page);
  });

  test('dashboard-baseline', async ({ page }) => {
    await gotoAndSettle(page, '/dashboard');

    // KPI cards muszą być widoczne — to nasz znacznik że strona dorenderowała
    await expect(page.locator('.rs-kpi-card').first()).toBeVisible({ timeout: 15_000 });
    await page.waitForTimeout(500);
    await freezeUi(page);

    await expect(page).toHaveScreenshot('dashboard.png', SCREENSHOT_OPTS);
  });

  test('payments-empty-list', async ({ page }) => {
    await gotoAndSettle(page, '/admin/payments');

    // KPI Payment-specific powinny być widoczne
    await expect(page.locator('.rs-pay-kpi').first()).toBeVisible({ timeout: 15_000 });

    // Spróbuj kliknąć filter "Wszystkie" jeśli istnieje (chip/tab)
    const allFilter = page
      .getByRole('tab', { name: /^Wszystkie$/i })
      .or(page.getByRole('button', { name: /^Wszystkie$/i }))
      .or(page.locator('.mud-chip').filter({ hasText: /^Wszystkie$/i }))
      .first();
    if (await allFilter.isVisible({ timeout: 1500 }).catch(() => false)) {
      await allFilter.click().catch(() => {});
      await page.waitForTimeout(600);
    }

    await freezeUi(page);
    await expect(page).toHaveScreenshot('payments-empty-list.png', SCREENSHOT_OPTS);
  });

  test('contracts-page-baseline', async ({ page }) => {
    await gotoAndSettle(page, '/admin/contracts');

    // Header "Umowy" jako sygnał gotowości
    const header = page
      .getByRole('heading', { name: /Umowy/i })
      .or(page.getByText(/^Umowy$/))
      .first();
    await expect(header).toBeVisible({ timeout: 15_000 });

    await page.waitForTimeout(600);
    await freezeUi(page);
    await expect(page).toHaveScreenshot('contracts-page.png', SCREENSHOT_OPTS);
  });

  test('schedule-month-view', async ({ page }) => {
    await gotoAndSettle(page, '/admin/schedule', 2000);

    // Kalendarz miesięczny — kandydaci na kontener
    const calendar = page
      .locator('.mud-cal, .mud-picker-calendar, [class*="calendar"], [class*="schedule"]')
      .first();
    await expect(calendar).toBeVisible({ timeout: 15_000 });

    await page.waitForTimeout(800);
    await freezeUi(page);
    await expect(page).toHaveScreenshot('schedule-month-view.png', SCREENSHOT_OPTS);
  });

  test('rental-edit-dialog', async ({ page }) => {
    await gotoAndSettle(page, '/admin/rentals');

    const clicked = await tryClickFirst(page, [
      /Nowy wynajem/i,
      /Dodaj wynajem/i,
      /\+ Nowy/i,
    ]);
    expect(clicked, 'Nie znaleziono przycisku "Nowy wynajem"').toBeTruthy();

    const dialog = page.locator('.mud-dialog, [role="dialog"]').first();
    await expect(dialog).toBeVisible({ timeout: 10_000 });
    await page.waitForTimeout(700);
    await freezeUi(page);

    await expect(page).toHaveScreenshot('rental-edit-dialog.png', SCREENSHOT_OPTS);
  });

  test('customer-edit-dialog', async ({ page }) => {
    await gotoAndSettle(page, '/admin/customers');

    const clicked = await tryClickFirst(page, [
      /Dodaj klienta/i,
      /Nowy klient/i,
      /Dodaj/i,
    ]);
    expect(clicked, 'Nie znaleziono przycisku "Dodaj klienta"').toBeTruthy();

    const dialog = page.locator('.mud-dialog, [role="dialog"]').first();
    await expect(dialog).toBeVisible({ timeout: 10_000 });
    await page.waitForTimeout(700);
    await freezeUi(page);

    await expect(page).toHaveScreenshot('customer-edit-dialog.png', SCREENSHOT_OPTS);
  });

  test('mark-as-paid-dialog', async ({ page }) => {
    await gotoAndSettle(page, '/admin/rentals');

    const markBtn = page
      .getByRole('button', { name: /Oznacz jako opłacone/i })
      .first();
    await expect(markBtn).toBeVisible({ timeout: 20_000 });
    await markBtn.scrollIntoViewIfNeeded();
    await markBtn.click();

    const dialog = page.locator('.mud-dialog').first();
    await expect(dialog).toBeVisible({ timeout: 10_000 });

    // Czekamy aż radio'y z opcjami płatności się wyrenderują
    await expect(dialog.getByText('Gotówka', { exact: false }).first()).toBeVisible({
      timeout: 10_000,
    });

    await page.waitForTimeout(700);
    await freezeUi(page);
    await expect(page).toHaveScreenshot('mark-as-paid-dialog.png', SCREENSHOT_OPTS);
  });

  test('dark-mode-dashboard', async ({ page }) => {
    await setDarkMode(page, true);
    await waitForBlazorReady(page);

    await gotoAndSettle(page, '/dashboard');

    await expect(page.locator('.rs-kpi-card').first()).toBeVisible({ timeout: 15_000 });

    // Sanity check — dark theme faktycznie aktywny
    const isDark = await page.evaluate(() => {
      const html = document.documentElement;
      return (
        html.classList.contains('mud-theme-dark') ||
        html.getAttribute('data-theme') === 'dark'
      );
    });
    expect(isDark, 'Dark theme nie został zaaplikowany').toBeTruthy();

    await page.waitForTimeout(600);
    await freezeUi(page);
    await expect(page).toHaveScreenshot('dark-mode-dashboard.png', SCREENSHOT_OPTS);
  });
});

test.describe('Visual regression — mobile baselines', () => {
  test.setTimeout(60_000);

  test.beforeEach(async ({ page }, testInfo) => {
    if (testInfo.project.name !== 'mobile') {
      testInfo.skip(true, 'Mobile baseline — uruchamiaj tylko na project=mobile');
    }
    await signInAsDemo(page);
    await waitForBlazorReady(page).catch(() => {});
  });

  test('mobile-dashboard', async ({ page }) => {
    await gotoAndSettle(page, '/dashboard');

    // KPI cards na mobile też są .rs-kpi-card (układają się w 1 kolumnę)
    await expect(page.locator('.rs-kpi-card').first()).toBeVisible({ timeout: 15_000 });

    await page.waitForTimeout(600);
    await freezeUi(page);
    await expect(page).toHaveScreenshot('mobile-dashboard.png', SCREENSHOT_OPTS);
  });

  test('mobile-customers-with-dialog', async ({ page }) => {
    await gotoAndSettle(page, '/admin/customers');

    // Na mobile przycisk dodawania może być FAB-em lub w menu
    let clicked = await tryClickFirst(page, [
      /Dodaj klienta/i,
      /Nowy klient/i,
      /Dodaj/i,
    ]);
    if (!clicked) {
      const fab = page.locator('.mud-fab, button:has-text("+")').first();
      if (await fab.isVisible({ timeout: 2000 }).catch(() => false)) {
        await fab.click();
        clicked = true;
      }
    }
    expect(clicked, 'Nie udało się otworzyć dialogu klienta na mobile').toBeTruthy();

    const dialog = page.locator('.mud-dialog, [role="dialog"]').first();
    await expect(dialog).toBeVisible({ timeout: 10_000 });
    await page.waitForTimeout(700);
    await freezeUi(page);

    await expect(page).toHaveScreenshot('mobile-customers-with-dialog.png', SCREENSHOT_OPTS);
  });
});
