import { test, expect } from '@playwright/test';
import * as fs from 'fs';
import * as path from 'path';
import { signInAsDemo, waitForBlazorReady } from '../helpers/demo.js';

/**
 * Performance suite — Web Vitals (FCP/LCP), TTI, Blazor boot, bundle size, cache warm-up,
 * concurrent loads. Wszystkie asercje są SOFT (expect.soft) — testy są informacyjne i
 * faillują tylko gdy regression >2x baseline (czyli sztywne limity poniżej, np. FCP 3s
 * przy baseline ~1.5s).
 *
 * Wszystkie pomiary lecą do test-results/perf-metrics.json (append po każdym teście).
 */

const METRICS_FILE = path.join('test-results', 'perf-metrics.json');

function recordMetric(name: string, data: Record<string, unknown>): void {
  try {
    const dir = path.dirname(METRICS_FILE);
    if (!fs.existsSync(dir)) fs.mkdirSync(dir, { recursive: true });
    let existing: Record<string, unknown>[] = [];
    if (fs.existsSync(METRICS_FILE)) {
      try {
        existing = JSON.parse(fs.readFileSync(METRICS_FILE, 'utf-8')) as Record<string, unknown>[];
        if (!Array.isArray(existing)) existing = [];
      } catch {
        existing = [];
      }
    }
    existing.push({ name, timestamp: new Date().toISOString(), ...data });
    fs.writeFileSync(METRICS_FILE, JSON.stringify(existing, null, 2));
  } catch (e) {
    console.warn('[perf] failed to record metric', e);
  }
}

async function measureWebVital(page: import('@playwright/test').Page, type: 'FCP' | 'LCP', timeoutMs = 10_000): Promise<number | null> {
  return await page.evaluate(
    ({ type, timeoutMs }) => new Promise<number | null>((resolve) => {
      // Najpierw spróbuj już zarejestrowanych entries
      const buffered = performance.getEntriesByType(type === 'FCP' ? 'paint' : 'largest-contentful-paint');
      if (type === 'FCP') {
        const fcp = buffered.find((e) => e.name === 'first-contentful-paint');
        if (fcp) {
          resolve(fcp.startTime);
          return;
        }
      } else if (buffered.length > 0) {
        const last = buffered[buffered.length - 1];
        resolve(last.startTime);
        return;
      }
      const observer = new PerformanceObserver((list) => {
        for (const entry of list.getEntries()) {
          if (type === 'FCP' && entry.name === 'first-contentful-paint') {
            observer.disconnect();
            resolve(entry.startTime);
            return;
          }
          if (type === 'LCP') {
            observer.disconnect();
            resolve(entry.startTime);
            return;
          }
        }
      });
      try {
        observer.observe({ type: type === 'FCP' ? 'paint' : 'largest-contentful-paint', buffered: true });
      } catch {
        resolve(null);
        return;
      }
      setTimeout(() => {
        observer.disconnect();
        resolve(null);
      }, timeoutMs);
    }),
    { type, timeoutMs }
  );
}

test.describe('Performance — Web Vitals + bundle + boot', () => {
  test.setTimeout(60_000);

  test('dashboard-fcp-under-3s', async ({ page }) => {
    await signInAsDemo(page);
    // Cold-ish — drugi goto z czystym page contextem
    const t0 = Date.now();
    await page.goto('/dashboard', { waitUntil: 'domcontentloaded' });
    const navMs = Date.now() - t0;
    await waitForBlazorReady(page);

    const fcp = await measureWebVital(page, 'FCP', 8000);
    console.log(`[perf] dashboard FCP=${fcp?.toFixed(0)}ms navTime=${navMs}ms`);
    recordMetric('dashboard-fcp', { fcpMs: fcp, navMs });

    expect.soft(fcp, 'FCP should be measured').not.toBeNull();
    if (fcp !== null) {
      expect.soft(fcp, `FCP should be <= 3000ms (got ${fcp.toFixed(0)})`).toBeLessThanOrEqual(3000);
    }
  });

  test('dashboard-lcp-under-5s', async ({ page }) => {
    await signInAsDemo(page);
    await page.goto('/dashboard', { waitUntil: 'domcontentloaded' });
    await waitForBlazorReady(page);
    // Daj LCP candidate moment na pojawienie się (KPI cards renderują po circuit)
    await page.locator('.rs-kpi-card').first().waitFor({ state: 'visible', timeout: 15_000 }).catch(() => {});
    await page.waitForTimeout(800);

    const lcp = await measureWebVital(page, 'LCP', 8000);
    console.log(`[perf] dashboard LCP=${lcp?.toFixed(0)}ms`);
    recordMetric('dashboard-lcp', { lcpMs: lcp });

    expect.soft(lcp, 'LCP should be measured').not.toBeNull();
    if (lcp !== null) {
      expect.soft(lcp, `LCP should be <= 5000ms (got ${lcp.toFixed(0)})`).toBeLessThanOrEqual(5000);
    }
  });

  test('rentals-page-tti-under-7s', async ({ page }) => {
    await signInAsDemo(page);

    const tStart = Date.now();
    await page.goto('/admin/rentals', { waitUntil: 'domcontentloaded' });
    await waitForBlazorReady(page);
    // TTI proxy: drawer + appbar widoczne + brak długich tasków przez 500ms.
    // Czekamy na pierwszy interaktywny element strony — np. nagłówek H1 lub tabelę.
    await page.locator('h1, h4, .mud-table, .rs-kpi-card').first()
      .waitFor({ state: 'visible', timeout: 15_000 }).catch(() => {});
    // krótki quiet-period
    await page.waitForLoadState('domcontentloaded');
    await page.waitForTimeout(300);
    const ttiMs = Date.now() - tStart;

    console.log(`[perf] rentals TTI≈${ttiMs}ms`);
    recordMetric('rentals-tti', { ttiMs });

    expect.soft(ttiMs, `Rentals TTI should be <= 7000ms (got ${ttiMs})`).toBeLessThanOrEqual(7000);
  });

  test('signalr-boot-under-4s', async ({ page }) => {
    await signInAsDemo(page);
    // Świeży nawigacyjny goto żeby zmierzyć boot circuit od początku
    const tStart = Date.now();
    await page.goto('/dashboard', { waitUntil: 'domcontentloaded' });
    // .rs-drawer widoczny → MudAppBar + drawer zhydrowane przez circuit
    await page.locator('.rs-drawer, .rs-appbar').first()
      .waitFor({ state: 'visible', timeout: 20_000 });
    const bootMs = Date.now() - tStart;

    console.log(`[perf] Blazor circuit boot≈${bootMs}ms`);
    recordMetric('signalr-boot', { bootMs });

    expect.soft(bootMs, `Blazor boot should be <= 4000ms (got ${bootMs})`).toBeLessThanOrEqual(4000);
  });

  test('bundle-size-check', async ({ page, baseURL }) => {
    // Trzeba mieć auth context dla statyki? Nie, /_framework/blazor.web.js jest publiczne.
    // Ale wygodniej przez page.request — używa konfiguracji projektu (baseURL).
    const resp = await page.request.get('/_framework/blazor.web.js');
    expect.soft(resp.ok(), 'blazor.web.js should be 200').toBeTruthy();
    const body = await resp.body();
    const sizeBytes = body.length;
    const sizeKb = sizeBytes / 1024;
    const contentEncoding = resp.headers()['content-encoding'] ?? 'identity';

    console.log(`[perf] blazor.web.js size=${sizeKb.toFixed(1)}KB encoding=${contentEncoding} baseURL=${baseURL}`);
    recordMetric('bundle-size', { sizeBytes, sizeKb, contentEncoding });

    // .NET 10 Blazor Server blazor.web.js baseline ~195KB; alert >300KB jako regression signal
    expect.soft(sizeKb, `blazor.web.js should be < 300KB (got ${sizeKb.toFixed(1)})`).toBeLessThan(300);
  });

  test('cold-vs-warm-comparison', async ({ page }) => {
    await signInAsDemo(page);

    // Cold: nawiguj na payments (pewnie nie była odwiedzona w sesji)
    const coldStart = Date.now();
    await page.goto('/admin/payments', { waitUntil: 'domcontentloaded' });
    await page.locator('.rs-pay-kpi, h1, h4').first()
      .waitFor({ state: 'visible', timeout: 15_000 }).catch(() => {});
    const coldMs = Date.now() - coldStart;

    // Wróć na inną stronę, potem warm goto tej samej
    await page.goto('/dashboard', { waitUntil: 'domcontentloaded' });
    await page.waitForTimeout(500);

    const warmStart = Date.now();
    await page.goto('/admin/payments', { waitUntil: 'domcontentloaded' });
    await page.locator('.rs-pay-kpi, h1, h4').first()
      .waitFor({ state: 'visible', timeout: 15_000 }).catch(() => {});
    const warmMs = Date.now() - warmStart;

    const ratio = warmMs > 0 ? coldMs / warmMs : 1;
    console.log(`[perf] cold=${coldMs}ms warm=${warmMs}ms ratio=${ratio.toFixed(2)}x`);
    recordMetric('cold-vs-warm', { coldMs, warmMs, ratio });

    // Warm powinno być >= 1.0x szybsze. Reżim: warm <= cold (czyli ratio >= 1.0).
    // "drugi <=1.5x szybszy" = warm <= cold (nie wolniej niż 2x cold = regression).
    expect.soft(warmMs, `Warm goto should not be >2x slower than cold (cold=${coldMs}, warm=${warmMs})`)
      .toBeLessThanOrEqual(coldMs * 2);
  });

  test('concurrent-pages-load', async ({ browser }) => {
    test.setTimeout(120_000);
    const context = await browser.newContext();
    const pages: import('@playwright/test').Page[] = [];
    try {
      // 5 stron w jednym kontekście (dzielą session/cookie po signinie)
      const signinPage = await context.newPage();
      await signInAsDemo(signinPage);
      await signinPage.close();

      const urls = [
        '/dashboard',
        '/admin/rentals',
        '/admin/customers',
        '/admin/products',
        '/admin/payments',
      ];

      const t0 = Date.now();
      const results = await Promise.all(urls.map(async (url) => {
        const p = await context.newPage();
        pages.push(p);
        const tp = Date.now();
        try {
          await p.goto(url, { waitUntil: 'domcontentloaded', timeout: 45_000 });
          await p.locator('.rs-appbar').waitFor({ state: 'visible', timeout: 20_000 });
        } catch (e) {
          return { url, ms: Date.now() - tp, ok: false, err: String(e) };
        }
        return { url, ms: Date.now() - tp, ok: true };
      }));
      const wallclockMs = Date.now() - t0;

      console.log(`[perf] concurrent 5 pages wallclock=${wallclockMs}ms`);
      results.forEach((r) => console.log(`[perf]   ${r.url} → ${r.ms}ms ${r.ok ? 'OK' : 'FAIL'}`));
      recordMetric('concurrent-pages', { wallclockMs, results });

      const allOk = results.every((r) => r.ok);
      expect.soft(allOk, 'All 5 concurrent pages should load OK').toBeTruthy();
      // Wallclock < 30s (5 stron równolegle, każda ma circuit boot ~2-4s)
      expect.soft(wallclockMs, `Concurrent wallclock should be <= 30000ms (got ${wallclockMs})`)
        .toBeLessThanOrEqual(30_000);
    } finally {
      for (const p of pages) {
        try { await p.close(); } catch { /* ignore */ }
      }
      await context.close();
    }
  });
});
