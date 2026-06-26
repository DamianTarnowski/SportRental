import { test, expect, devices } from '@playwright/test';
import { signInAsDemo, waitForBlazorReady } from '../helpers/demo';

// Force iPhone 14 viewport for all tests in this file
test.use({ ...devices['iPhone 14'] });

test.describe('Mobile viewport — iPhone 14', () => {
  test.setTimeout(60_000);

  const pages: Array<{ path: string; key: string }> = [
    { path: '/dashboard', key: 'dashboard' },
    { path: '/admin/rentals', key: 'rentals' },
    { path: '/admin/payments', key: 'payments' },
    { path: '/admin/contracts', key: 'contracts' },
    { path: '/admin/customers', key: 'customers' },
    { path: '/admin/products', key: 'products' },
    { path: '/admin/equipment-handling', key: 'equipment-handling' },
    { path: '/admin/schedule', key: 'schedule' },
  ];

  for (const { path, key } of pages) {
    test(`no horizontal scroll on ${key}`, async ({ page, viewport }) => {
      test.setTimeout(60_000);

      await signInAsDemo(page);

      await page.goto(path, { waitUntil: 'networkidle' });
      await waitForBlazorReady(page);
      // Daj chwilę na late layout shifts (MudBlazor renderuje components po circuit ready)
      await page.waitForTimeout(800);

      const viewportWidth = viewport?.width ?? 390;
      const scrollWidth = await page.evaluate(
        () => document.documentElement.scrollWidth,
      );
      const clientWidth = await page.evaluate(
        () => document.documentElement.clientWidth,
      );

      // Topbar must be visible
      await expect(page.locator('.rs-appbar')).toBeVisible();

      // No horizontal overflow (2px tolerance for sub-pixel rounding)
      expect(
        scrollWidth,
        `Horizontal overflow on ${path}: scrollWidth=${scrollWidth} viewport=${viewportWidth} client=${clientWidth}`,
      ).toBeLessThanOrEqual(viewportWidth + 2);

      await page.screenshot({
        path: `test-results/mobile-${key}.png`,
        fullPage: true,
      });
    });
  }

  test('CustomerEditDialog renders cleanly on mobile', async ({ page }) => {
    test.setTimeout(60_000);

    await signInAsDemo(page);

    await page.goto('/admin/customers', { waitUntil: 'networkidle' });
    await waitForBlazorReady(page);
    await page.waitForTimeout(800);

    // Próbujemy klikać przycisk "Dodaj klienta" / "Nowy klient" w wielu wariantach
    const addCandidates = [
      page.getByRole('button', { name: /dodaj klienta/i }),
      page.getByRole('button', { name: /nowy klient/i }),
      page.getByRole('link', { name: /dodaj klienta/i }),
      page.getByRole('link', { name: /nowy klient/i }),
      page.getByRole('button', { name: /dodaj/i }),
      page.locator('button:has(.mud-icon-button-label), .mud-fab').first(),
    ];

    let clicked = false;
    for (const cand of addCandidates) {
      try {
        const first = cand.first();
        if (await first.isVisible({ timeout: 1500 }).catch(() => false)) {
          await first.click({ timeout: 3000 });
          clicked = true;
          break;
        }
      } catch {
        // try next
      }
    }

    if (!clicked) {
      // Awaryjnie — FAB lub przycisk + (plus icon)
      const fab = page.locator('.mud-fab, button:has-text("+")').first();
      if (await fab.isVisible({ timeout: 2000 }).catch(() => false)) {
        await fab.click();
        clicked = true;
      }
    }

    expect(clicked, 'Nie udało się znaleźć przycisku dodawania klienta').toBeTruthy();

    // Czekamy aż dialog MudBlazor się pokaże
    const dialog = page.locator('.mud-dialog, [role="dialog"]').first();
    await expect(dialog).toBeVisible({ timeout: 10_000 });
    await page.waitForTimeout(500);

    // Sprawdź typowe pola formularza (z fallbackiem labels w PL)
    const expectedFields = [
      /imię|imie|name/i,
      /email|e-mail/i,
      /telefon|phone/i,
      /(nr|numer)\s*dokumentu|dokument/i,
      /adres|address/i,
      /notatki|notes/i,
    ];

    const dialogScrollWidth = await dialog.evaluate(
      (el) => (el as HTMLElement).scrollWidth,
    );
    const dialogClientWidth = await dialog.evaluate(
      (el) => (el as HTMLElement).clientWidth,
    );

    // Dialog content shouldn't overflow horizontally
    expect(
      dialogScrollWidth,
      `Dialog horizontal overflow: scroll=${dialogScrollWidth} client=${dialogClientWidth}`,
    ).toBeLessThanOrEqual(dialogClientWidth + 2);

    // Verify at least 3 of expected fields are present (some forms may have different labels)
    let fieldsFound = 0;
    for (const pattern of expectedFields) {
      const byLabel = dialog.getByLabel(pattern).first();
      const byText = dialog.getByText(pattern).first();
      const byPlaceholder = dialog.getByPlaceholder(pattern).first();

      const exists =
        (await byLabel.count().catch(() => 0)) > 0 ||
        (await byText.count().catch(() => 0)) > 0 ||
        (await byPlaceholder.count().catch(() => 0)) > 0;

      if (exists) {
        fieldsFound++;
      }
    }

    expect(
      fieldsFound,
      `Oczekiwano przynajmniej 3 pól formularza, znaleziono ${fieldsFound}`,
    ).toBeGreaterThanOrEqual(3);

    await page.screenshot({
      path: 'test-results/customer-dialog-mobile.png',
      fullPage: true,
    });
  });
});
