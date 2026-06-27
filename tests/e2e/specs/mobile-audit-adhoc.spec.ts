import { test, devices } from '@playwright/test';
import { signInAsDemo, waitForBlazorReady, setDarkMode } from '../helpers/demo';

/**
 * Ad-hoc mobile audit — rozszerzony screenshot set dla wizualnej review.
 * Wszystkie kluczowe strony + dialogi w light i dark mode na iPhone 14.
 */

test.use({ ...devices['iPhone 14'] });
test.describe.configure({ mode: 'serial' });

const PAGES = [
  { path: '/dashboard', name: 'dashboard' },
  { path: '/admin/rentals', name: 'rentals' },
  { path: '/admin/payments', name: 'payments' },
  { path: '/admin/contracts', name: 'contracts' },
  { path: '/admin/customers', name: 'customers' },
  { path: '/admin/products', name: 'products' },
  { path: '/admin/equipment-handling', name: 'equipment-handling' },
  { path: '/admin/schedule', name: 'schedule' },
  { path: '/admin/reports', name: 'reports' },
  { path: '/admin/company-settings', name: 'company-settings' },
];

test.describe('Mobile audit — light mode', () => {
  test('all pages light', async ({ page }) => {
    test.setTimeout(180_000);
    await signInAsDemo(page);
    for (const p of PAGES) {
      await page.goto(p.path, { waitUntil: 'domcontentloaded' });
      await waitForBlazorReady(page);
      await page.waitForTimeout(1200);
      await page.screenshot({ path: `test-results/mobile-audit/light-${p.name}.png`, fullPage: false });
    }
  });

  test('open hamburger menu', async ({ page }) => {
    test.setTimeout(60_000);
    await signInAsDemo(page);
    await page.goto('/dashboard', { waitUntil: 'domcontentloaded' });
    await waitForBlazorReady(page);
    await page.waitForTimeout(800);
    const burger = page.getByRole('button', { name: /menu|Otwórz/i }).first();
    await burger.click().catch(() => {});
    await page.waitForTimeout(500);
    await page.screenshot({ path: 'test-results/mobile-audit/light-drawer-open.png', fullPage: false });
  });

  test('open new rental dialog', async ({ page }) => {
    test.setTimeout(90_000);
    await signInAsDemo(page);
    await page.goto('/admin/rentals', { waitUntil: 'domcontentloaded' });
    await waitForBlazorReady(page);
    await page.waitForTimeout(1500);
    const newBtn = page
      .getByRole('button', { name: /nowy wynajem|dodaj wynajem/i })
      .or(page.locator('.mud-fab'))
      .first();
    if (await newBtn.isVisible({ timeout: 5_000 }).catch(() => false)) {
      await newBtn.click();
      await page.waitForTimeout(800);
      await page.screenshot({ path: 'test-results/mobile-audit/light-new-rental-dialog.png', fullPage: false });
    }
  });

  test('open new customer dialog', async ({ page }) => {
    test.setTimeout(90_000);
    await signInAsDemo(page);
    await page.goto('/admin/customers', { waitUntil: 'domcontentloaded' });
    await waitForBlazorReady(page);
    await page.waitForTimeout(1200);
    const addBtn = page
      .getByRole('button', { name: /dodaj klienta|nowy klient/i })
      .or(page.locator('.mud-fab, [aria-label*="Dodaj"]'))
      .first();
    if (await addBtn.isVisible({ timeout: 5_000 }).catch(() => false)) {
      await addBtn.click();
      await page.waitForTimeout(800);
      await page.screenshot({ path: 'test-results/mobile-audit/light-new-customer-dialog.png', fullPage: false });
    }
  });
});

test.describe('Mobile audit — dark mode', () => {
  test('all pages dark', async ({ page }) => {
    test.setTimeout(180_000);
    await signInAsDemo(page);
    await setDarkMode(page, true);
    for (const p of PAGES.slice(0, 6)) {
      await page.goto(p.path, { waitUntil: 'domcontentloaded' });
      await waitForBlazorReady(page);
      await page.waitForTimeout(1200);
      await page.screenshot({ path: `test-results/mobile-audit/dark-${p.name}.png`, fullPage: false });
    }
  });
});
