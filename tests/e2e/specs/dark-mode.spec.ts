import { test, expect } from '@playwright/test';
import { signInAsDemo, waitForBlazorReady, setDarkMode } from '../helpers/demo';

test.describe('dark-mode', () => {
  test('theme-toggle-persists', async ({ page }) => {
    test.setTimeout(60_000);

    await signInAsDemo(page);
    await waitForBlazorReady(page);

    // Light screenshot (default state)
    await page.screenshot({ path: 'test-results/dark-mode/dashboard-light.png', fullPage: true });

    // Toggle dark via localStorage helper (reloads page).
    await setDarkMode(page, true);
    await waitForBlazorReady(page);

    // Verify dark mode applied — fallbacks: mud-theme-dark class on body OR html OR data-theme attribute.
    const isDark = await page.evaluate(() => {
      const body = document.body;
      const html = document.documentElement;
      const hasDarkClass =
        body.classList.contains('mud-theme-dark') ||
        html.classList.contains('mud-theme-dark') ||
        body.getAttribute('data-theme') === 'dark' ||
        html.getAttribute('data-theme') === 'dark';
      // Fallback: MudBlazor often sets the theme via inline styles / CSS vars on body — check bg color.
      const bg = getComputedStyle(body).backgroundColor;
      // Parse rgb; consider dark if avg channel < 128.
      const m = bg.match(/\d+/g);
      const avg = m && m.length >= 3 ? (Number(m[0]) + Number(m[1]) + Number(m[2])) / 3 : 255;
      return hasDarkClass || avg < 128;
    });
    expect(isDark, 'Expected dark theme to be active after setDarkMode(true)').toBeTruthy();

    await page.screenshot({ path: 'test-results/dark-mode/dashboard-dark.png', fullPage: true });

    // Reload — persistence check.
    await page.reload({ waitUntil: 'networkidle' });
    await waitForBlazorReady(page);

    const stillDark = await page.evaluate(() => {
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
      const ls = localStorage.getItem('rs-theme-dark');
      return { darkActive: hasDarkClass || avg < 128, ls };
    });
    expect(stillDark.ls, 'localStorage rs-theme-dark should persist').toBe('1');
    expect(stillDark.darkActive, 'Dark theme should still be active after reload').toBeTruthy();

    await page.screenshot({
      path: 'test-results/dark-mode/dashboard-dark-after-reload.png',
      fullPage: true,
    });
  });

  test('dark-on-all-key-pages', async ({ page }) => {
    test.setTimeout(60_000);

    await signInAsDemo(page);
    await waitForBlazorReady(page);
    await setDarkMode(page, true);
    await waitForBlazorReady(page);

    const pages: Array<{ url: string; file: string }> = [
      { url: '/admin/rentals', file: 'rentals-dark.png' },
      { url: '/admin/payments', file: 'payments-dark.png' },
      { url: '/admin/contracts', file: 'contracts-dark.png' },
      { url: '/admin/customers', file: 'customers-dark.png' },
    ];

    for (const p of pages) {
      await page.goto(p.url, { waitUntil: 'networkidle' });
      await waitForBlazorReady(page);
      // Give grids / queries a moment to render before snapshot.
      await page.waitForTimeout(800);
      await page.screenshot({
        path: `test-results/dark-mode/${p.file}`,
        fullPage: true,
      });

      // Sanity: page should be in dark mode (body bg dark-ish).
      const bgAvg = await page.evaluate(() => {
        const bg = getComputedStyle(document.body).backgroundColor;
        const m = bg.match(/\d+/g);
        return m && m.length >= 3 ? (Number(m[0]) + Number(m[1]) + Number(m[2])) / 3 : 255;
      });
      expect(bgAvg, `Page ${p.url} should have dark body bg`).toBeLessThan(160);
    }
  });

  test('dark-mode-no-white-bleed', async ({ page }) => {
    test.setTimeout(60_000);

    await signInAsDemo(page);
    await waitForBlazorReady(page);
    await setDarkMode(page, true);
    await waitForBlazorReady(page);

    await page.goto('/admin/products', { waitUntil: 'networkidle' });
    await waitForBlazorReady(page);
    await page.waitForTimeout(1000);

    // Collect background colors of all visible MudPaper-like elements.
    const offenders = await page.evaluate(() => {
      const out: Array<{ idx: number; tag: string; classes: string; bg: string; text?: string }> = [];
      const els = Array.from(document.querySelectorAll('.mud-paper')) as HTMLElement[];
      els.forEach((el, idx) => {
        const rect = el.getBoundingClientRect();
        const visible = rect.width > 0 && rect.height > 0;
        if (!visible) return;
        const cs = getComputedStyle(el);
        const bg = cs.backgroundColor;
        if (bg === 'rgb(255, 255, 255)' || bg === '#ffffff' || bg === 'rgb(255,255,255)') {
          out.push({
            idx,
            tag: el.tagName,
            classes: el.className,
            bg,
            text: (el.innerText || '').slice(0, 80),
          });
        }
      });
      return out;
    });

    if (offenders.length > 0) {
      // Helpful diagnostic screenshot.
      await page.screenshot({
        path: 'test-results/dark-mode/products-dark-white-bleed.png',
        fullPage: true,
      });
    }

    expect(
      offenders,
      `Found ${offenders.length} MudPaper(s) with pure white bg in dark mode:\n` +
        JSON.stringify(offenders, null, 2),
    ).toEqual([]);
  });
});
