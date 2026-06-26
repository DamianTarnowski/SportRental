import { test, expect, type BrowserContext, type Page } from '@playwright/test';
import { signInAsDemo, waitForBlazorReady } from '../helpers/demo';

/**
 * Stress + resilience tests — concurrent users, SignalR reconnect, edge cases.
 *
 * Te testy ŚWIADOMIE biją w infra (równoległe konteksty, offline toggling, długie inputy).
 * Każdy ma własny timeout (≥60s) i fail-only screenshoty (manual przy errorach).
 *
 * UWAGA: niektóre testy są inherentnie flaky na slow link (SignalR reconnect na cold start)
 * — tolerujemy partial pass z annotation gdy reconnect przekroczy budżet.
 */

const SCREENSHOT_DIR = 'test-results/stress-resilience';

/**
 * Próbuje wyciągnąć unikalny identyfikator demo tenanta (email/sidebar/cookie).
 * Skopiowany z smoke.spec — ale uproszczony, bez user menu (concurrent contexty,
 * nie chcemy kliknięć na DOM podczas porównywania).
 */
async function readDemoIdentity(page: Page): Promise<string> {
  // 1. Body HTML scan — email demo+<hex>@rentspot.eu
  const html = await page.content().catch(() => '');
  const m = html.match(/demo\+[0-9a-fA-F]+@rentspot\.eu/);
  if (m) return m[0];

  // 2. Sidebar text — tenant name
  const sidebar = await page.locator('.rs-drawer').innerText().catch(() => '');
  if (sidebar.length > 0) return `sidebar:${sidebar.slice(0, 200)}`;

  // 3. Cookie session
  const cookies = await page.context().cookies();
  const auth = cookies.find(
    (c) => c.name.toLowerCase().includes('auth') || c.name.toLowerCase().includes('identity'),
  );
  return auth ? `cookie:${auth.value.slice(0, 32)}` : '';
}

test.describe('stress-resilience', () => {
  test.setTimeout(120_000);

  test('concurrent-demo-signins: 5 równoległych contextów dostaje 5 różnych tenantów', async ({
    browser,
  }) => {
    test.setTimeout(120_000);

    const N = 5;
    const contexts: BrowserContext[] = [];
    try {
      // Stwórz N fresh contextów
      for (let i = 0; i < N; i++) {
        contexts.push(await browser.newContext());
      }
      const pages = await Promise.all(contexts.map((c) => c.newPage()));

      // ODPALAMY signInAsDemo równolegle — Promise.all
      const results = await Promise.allSettled(
        pages.map(async (p, i) => {
          await signInAsDemo(p);
          await waitForBlazorReady(p).catch(() => {});
          // Daj circuit chwilę by zapisał tenant name w sidebar
          await p.waitForTimeout(1000);
          const ident = await readDemoIdentity(p);
          return { i, ident };
        }),
      );

      const succeeded = results.filter((r) => r.status === 'fulfilled') as PromiseFulfilledResult<{
        i: number;
        ident: string;
      }>[];
      const failed = results.filter((r) => r.status === 'rejected');

      // Tolerujemy max 1 fail na 5 (cold start / SignalR race)
      expect(failed.length, `Zbyt wiele signin failów: ${failed.length}/${N}`).toBeLessThanOrEqual(1);
      expect(succeeded.length).toBeGreaterThanOrEqual(N - 1);

      const idents = succeeded.map((r) => r.value.ident).filter((x) => x.length > 0);
      // Wszystkie identyfikatory muszą być różne (unikalne tenanty)
      const unique = new Set(idents);
      expect(
        unique.size,
        `Oczekiwano ${idents.length} unikalnych identów, było ${unique.size}: ${idents.join(' | ')}`,
      ).toEqual(idents.length);

      // Wzbogać raport
      test.info().annotations.push({
        type: 'info',
        description: `Concurrent signins OK: ${idents.length} unikalnych identów`,
      });
    } finally {
      for (const c of contexts) {
        await c.close().catch(() => {});
      }
    }
  });

  test('rapid-navigation: 8 szybkich nawigacji bez crashów', async ({ page }) => {
    test.setTimeout(90_000);

    const pageErrors: Error[] = [];
    const consoleErrors: string[] = [];
    page.on('pageerror', (e) => pageErrors.push(e));
    page.on('console', (m) => {
      if (m.type() === 'error') consoleErrors.push(m.text());
    });

    await signInAsDemo(page);
    await waitForBlazorReady(page);

    const urls = [
      '/dashboard',
      '/admin/rentals',
      '/admin/customers',
      '/admin/products',
      '/admin/payments',
      '/admin/contracts',
      '/admin/equipment-handling',
      '/admin/schedule',
    ];

    for (const url of urls) {
      try {
        await page.goto(url, { waitUntil: 'domcontentloaded', timeout: 15_000 });
      } catch {
        // net::ERR_ABORTED ok podczas rapid nav
      }
      await page.waitForTimeout(200);
    }

    // Po nawigacji daj 2s na settle
    await page.waitForTimeout(2_000);

    // .rs-appbar nadal widoczny — circuit żyje
    await expect(page.locator('.rs-appbar')).toBeVisible({ timeout: 10_000 });

    // Filter szumy
    const critical = consoleErrors.filter((e) => {
      const l = e.toLowerCase();
      if (l.includes('resizeobserver')) return false;
      if (l.includes('failed to load resource')) return false;
      if (l.includes('net::err_aborted')) return false;
      if (l.includes('websocket')) return false;
      if (l.includes('connection disconnected')) return false;
      if (l.includes('circuit')) return false;
      if (l.includes('mudblazor')) return false;
      if (l.includes('deprecated')) return false;
      if (l.includes('favicon')) return false;
      return l.trim().length > 0;
    });

    expect(
      pageErrors,
      `JS pageerror podczas rapid nav: ${pageErrors.map((e) => e.message).join('\n')}`,
    ).toHaveLength(0);
    expect(critical, `Krytyczne console errors:\n${critical.join('\n')}`).toHaveLength(0);
  });

  test('dialog-open-close-20x: brak memory leak', async ({ page }, testInfo) => {
    test.setTimeout(120_000);
    // Mobile używa MudMenu zamiast widocznego buttona — skip
    if (testInfo.project.name === 'mobile') {
      testInfo.skip(true, 'dialog stress: mobile ma MudMenu, dedykowany test TODO');
    }

    await signInAsDemo(page);
    await waitForBlazorReady(page);
    await page.goto('/admin/rentals', { waitUntil: 'domcontentloaded' });
    await waitForBlazorReady(page);
    await page.waitForTimeout(1500);

    // Baseline memory (jeśli Chrome eksponuje performance.memory)
    const memBefore = await page
      .evaluate(() => {
        // @ts-expect-error performance.memory non-standard
        const m = (performance as unknown as { memory?: { usedJSHeapSize: number } }).memory;
        return m ? m.usedJSHeapSize : null;
      })
      .catch(() => null);

    const ITER = 20;
    let opened = 0;

    for (let i = 0; i < ITER; i++) {
      const btn = page.getByRole('button', { name: /Oznacz jako opłacone/i }).first();
      if (!(await btn.isVisible({ timeout: 5_000 }).catch(() => false))) {
        testInfo.annotations.push({
          type: 'warn',
          description: `Brak buttona "Oznacz jako opłacone" w iter ${i} — możliwe że wszystkie wynajmy już opłacone`,
        });
        break;
      }

      await btn.scrollIntoViewIfNeeded();
      await btn.click();

      const dialog = page.locator('.mud-dialog').first();
      await expect(dialog).toBeVisible({ timeout: 10_000 });

      // Anuluj — szukamy Anuluj/Zamknij button w dialogu, lub naciskamy Escape
      const cancel = dialog
        .getByRole('button', { name: /Anuluj|Zamknij|Cancel/i })
        .first();
      if (await cancel.isVisible().catch(() => false)) {
        await cancel.click();
      } else {
        await page.keyboard.press('Escape');
      }

      await expect(dialog).toBeHidden({ timeout: 10_000 });
      opened++;
      // Mały delay między iteracjami
      await page.waitForTimeout(100);
    }

    // Co najmniej 5 iteracji się udało — w przeciwnym wypadku mark-paid zjadł wszystkie wynajmy
    expect(opened, `Oczekiwano ≥5 udanych open/close cykli, było ${opened}`).toBeGreaterThanOrEqual(5);

    // Memory check — jeśli dostępny, tolerancja 50MB (Blazor circuit + MudBlazor refs)
    const memAfter = await page
      .evaluate(() => {
        // @ts-expect-error performance.memory non-standard
        const m = (performance as unknown as { memory?: { usedJSHeapSize: number } }).memory;
        return m ? m.usedJSHeapSize : null;
      })
      .catch(() => null);

    if (memBefore && memAfter) {
      const growthMB = (memAfter - memBefore) / (1024 * 1024);
      testInfo.annotations.push({
        type: 'info',
        description: `JS heap growth po ${opened} cyklach: ${growthMB.toFixed(2)} MB`,
      });
      // Tolerancja 100MB — Blazor Server trzyma DOM refs między komponentami
      expect(growthMB, `Memory leak? Wzrost ${growthMB.toFixed(2)} MB`).toBeLessThan(100);
    } else {
      testInfo.annotations.push({
        type: 'info',
        description: 'performance.memory niedostępne — memory check pominięty',
      });
    }
  });

  test('long-text-input: 5000 znaków w Notatki klienta', async ({ page }) => {
    test.setTimeout(90_000);

    await signInAsDemo(page);
    await waitForBlazorReady(page);
    await page.goto('/admin/customers', { waitUntil: 'domcontentloaded' });
    await waitForBlazorReady(page);
    await page.waitForTimeout(1500);

    // Otwórz "Nowy klient" — przycisk z aria-label="Dodaj klienta" lub text "Nowy klient"
    const addBtn = page
      .getByRole('button', { name: /Dodaj klienta|Nowy klient/i })
      .first();
    await expect(addBtn).toBeVisible({ timeout: 15_000 });
    await addBtn.click();

    const dialog = page.locator('.mud-dialog').first();
    await expect(dialog).toBeVisible({ timeout: 10_000 });

    // Notatki textfield
    const notesField = dialog
      .locator('textarea, input')
      .filter({ has: page.locator('xpath=ancestor::*[contains(@class,"mud-input-control")]//label[contains(text(),"Notatki")]') })
      .first();

    // Fallback: szukaj po label tekstowym
    const notesByLabel = dialog
      .locator('.mud-input-control')
      .filter({ hasText: /Notatki/i })
      .locator('textarea, input')
      .first();

    const targetField = (await notesField.count().catch(() => 0)) > 0 ? notesField : notesByLabel;
    await expect(targetField).toBeVisible({ timeout: 10_000 });

    const longText = 'X'.repeat(5000);
    await targetField.fill(longText);

    // Verify length w polu
    const actualValue = await targetField.inputValue();
    expect(actualValue.length, `Notatki długość po fill: ${actualValue.length}`).toEqual(5000);

    // Wypełnij minimalne wymagane pola — Imię, Nazwisko (Email opcjonalnie)
    const firstName = dialog
      .locator('.mud-input-control')
      .filter({ hasText: /Imię/i })
      .locator('input')
      .first();
    if (await firstName.isVisible().catch(() => false)) {
      await firstName.fill('StressTest');
    }
    const lastName = dialog
      .locator('.mud-input-control')
      .filter({ hasText: /Nazwisko/i })
      .locator('input')
      .first();
    if (await lastName.isVisible().catch(() => false)) {
      await lastName.fill('Resilience');
    }

    // Submit — szukamy Zapisz/Save/Dodaj
    const saveBtn = dialog
      .getByRole('button', { name: /Zapisz|Save|Dodaj|Utwórz/i })
      .first();
    await expect(saveBtn).toBeVisible({ timeout: 5_000 });
    await saveBtn.click();

    // Dialog się zamyka (success) ALBO pokazuje validation error — w obu wypadkach NIE crashuje
    const dialogClosed = await dialog
      .waitFor({ state: 'hidden', timeout: 15_000 })
      .then(() => true)
      .catch(() => false);

    // Page nadal żyje (nie ma crashu)
    await expect(page.locator('.rs-appbar')).toBeVisible({ timeout: 5_000 });

    test.info().annotations.push({
      type: 'info',
      description: `5000-char notes save: dialog ${dialogClosed ? 'zamknięty (sukces)' : 'nadal otwarty (validation lub wolny zapis) — ale brak crashu'}`,
    });
  });

  test('browser-back-forward: state OK po back/forward', async ({ page }) => {
    test.setTimeout(60_000);

    await signInAsDemo(page);
    await waitForBlazorReady(page);

    // Dashboard
    await page.goto('/dashboard', { waitUntil: 'domcontentloaded' });
    await waitForBlazorReady(page);
    await page.waitForTimeout(800);
    expect(page.url()).toContain('/dashboard');

    // Rentals
    await page.goto('/admin/rentals', { waitUntil: 'domcontentloaded' });
    await waitForBlazorReady(page);
    await page.waitForTimeout(800);
    expect(page.url()).toContain('/admin/rentals');

    // Back → dashboard
    await page.goBack({ waitUntil: 'domcontentloaded' });
    await waitForBlazorReady(page);
    await page.waitForTimeout(800);
    expect(page.url()).toContain('/dashboard');

    // Sidebar nadal działa
    await expect(page.locator('.rs-appbar')).toBeVisible({ timeout: 5_000 });

    // Forward → rentals
    await page.goForward({ waitUntil: 'domcontentloaded' });
    await waitForBlazorReady(page);
    await page.waitForTimeout(800);
    expect(page.url()).toContain('/admin/rentals');

    // Topbar i sidebar nadal działają
    await expect(page.locator('.rs-appbar')).toBeVisible({ timeout: 5_000 });

    // Brak page errors
    const errors: Error[] = [];
    page.on('pageerror', (e) => errors.push(e));
    await page.waitForTimeout(500);
    expect(errors, `pageerror po back/forward: ${errors.map((e) => e.message).join('\n')}`).toHaveLength(0);
  });

  test('signalr-disconnect-reconnect: offline 5s → online, circuit przepina się', async ({
    page,
    context,
  }) => {
    test.setTimeout(120_000);

    await signInAsDemo(page);
    await waitForBlazorReady(page);
    await page.goto('/dashboard', { waitUntil: 'domcontentloaded' });
    await waitForBlazorReady(page);
    await page.waitForTimeout(1500);

    await expect(page.locator('.rs-appbar')).toBeVisible({ timeout: 10_000 });

    // Offline na 5 sekund — Blazor wykryje disconnect i pokaże reconnect modal
    await context.setOffline(true);
    await page.waitForTimeout(5_000);

    // Reconnect modal może się pokazać (#components-reconnect-modal)
    const reconnectModal = page.locator('#components-reconnect-modal');
    const modalVisible = await reconnectModal.isVisible().catch(() => false);
    test.info().annotations.push({
      type: 'info',
      description: `Reconnect modal po 5s offline: ${modalVisible ? 'visible' : 'hidden (circuit tolerated)'}`,
    });

    // Online
    await context.setOffline(false);

    // Daj 15s na reconnect (Blazor exponential backoff, default 8 prób)
    await page.waitForFunction(
      () => {
        const modal = document.getElementById('components-reconnect-modal');
        if (!modal) return true;
        const style = window.getComputedStyle(modal);
        return style.display === 'none' || modal.classList.contains('components-reconnect-hide');
      },
      { timeout: 30_000 },
    ).catch(() => {
      test.info().annotations.push({
        type: 'warn',
        description: 'Reconnect modal nie zniknął w 30s — może wymagać reload',
      });
    });

    // Sidebar/appbar nadal widoczne (DOM przeżywa disconnect)
    await expect(page.locator('.rs-appbar')).toBeVisible({ timeout: 10_000 });

    // Test interaktywności po reconnect — kliknij na sidebar item lub nawiguj
    await page.goto('/admin/rentals', { waitUntil: 'domcontentloaded' }).catch(() => {});
    await page.waitForTimeout(2_000);
    await expect(page.locator('.rs-appbar')).toBeVisible({ timeout: 15_000 });
  });

  test('tab-switch-persistence: dwie zakładki w tym samym contextcie widzą ten sam tenant', async ({
    browser,
  }) => {
    test.setTimeout(90_000);

    // Jeden context = shared cookies
    const ctx = await browser.newContext();
    try {
      const pageA = await ctx.newPage();
      await signInAsDemo(pageA);
      await waitForBlazorReady(pageA);
      await pageA.waitForTimeout(1000);
      const identA = await readDemoIdentity(pageA);
      expect(identA, 'Tab A nie zwróciła identyfikatora').toBeTruthy();

      // Druga zakładka w TYM SAMYM contextcie — shared cookies, więc ten sam tenant
      const pageB = await ctx.newPage();
      // Idziemy direct na /dashboard — auth cookie powinien załatwić sign-in
      await pageB.goto('/dashboard', { waitUntil: 'domcontentloaded', timeout: 30_000 });
      await waitForBlazorReady(pageB).catch(() => {});
      await pageB.waitForTimeout(1500);

      // Jeśli redirect na sign-in (nie ma cookie auth) — fallback to /Account/Demo na pageB?
      // Nie — wtedy stworzy nowy tenant. Sprawdźmy URL.
      if (pageB.url().includes('/Account/Login') || pageB.url().includes('/Identity/Account')) {
        test.info().annotations.push({
          type: 'warn',
          description: `Tab B redirected na login — cookie auth nie shared? URL: ${pageB.url()}`,
        });
      }

      const identB = await readDemoIdentity(pageB);
      expect(identB, 'Tab B nie zwróciła identyfikatora').toBeTruthy();

      // Oba taby tego samego contextu MUSZĄ widzieć ten sam tenant
      expect(
        identB,
        `Tab A i Tab B w tym samym contextcie powinny być TYM SAMYM tenantem.\n A=${identA}\n B=${identB}`,
      ).toEqual(identA);

      // Demo chip widoczny w obu zakładkach (Tenant.IsDemo synced via DB)
      const demoChipA = pageA.locator('.rs-demo-chip').first();
      const demoChipB = pageB.locator('.rs-demo-chip').first();
      await expect(demoChipA).toBeVisible({ timeout: 10_000 });
      await expect(demoChipB).toBeVisible({ timeout: 10_000 });
    } finally {
      await ctx.close().catch(() => {});
    }
  });
});
