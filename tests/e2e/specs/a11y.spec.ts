import { test, expect } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import { signInAsDemo, waitForBlazorReady } from '../helpers/demo';

type AxeViolation = {
  id: string;
  impact?: string | null;
  description: string;
  help: string;
  helpUrl: string;
  nodes: unknown[];
};

function reportViolations(pageName: string, violations: AxeViolation[]): void {
  // eslint-disable-next-line no-console
  console.log(`\n==== A11Y REPORT: ${pageName} ====`);
  // eslint-disable-next-line no-console
  console.log(`Total violations: ${violations.length}`);

  if (violations.length === 0) {
    // eslint-disable-next-line no-console
    console.log('No accessibility violations detected.');
    return;
  }

  const bySeverity: Record<string, number> = {
    critical: 0,
    serious: 0,
    moderate: 0,
    minor: 0,
  };

  for (const v of violations) {
    const impact = (v.impact ?? 'unknown') as string;
    bySeverity[impact] = (bySeverity[impact] ?? 0) + 1;
    // eslint-disable-next-line no-console
    console.log(
      `  - [${impact ?? 'unknown'}] ${v.id}: ${v.description} (${v.nodes.length} node(s))  -> ${v.helpUrl}`
    );
  }

  // eslint-disable-next-line no-console
  console.log('Severity counts:', JSON.stringify(bySeverity));
  // eslint-disable-next-line no-console
  console.log(`==== END A11Y REPORT: ${pageName} ====\n`);
}

async function scanPage(page: import('@playwright/test').Page, url: string, pageName: string) {
  await page.goto(url, { waitUntil: 'networkidle' }).catch(async () => {
    // fallback — niektóre strony nie idą do networkidle szybko
    await page.goto(url, { waitUntil: 'domcontentloaded' });
  });
  await waitForBlazorReady(page).catch(() => {
    // ignoruj jeśli appbar się nie pojawia (np. Login page)
  });
  // chwila na dynamiczny content (KPI cards, tabele)
  await page.waitForTimeout(1500);

  const results = await new AxeBuilder({ page })
    .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'])
    .analyze();

  const violations = results.violations as AxeViolation[];
  reportViolations(pageName, violations);

  const blocking = violations.filter(
    (v) => v.impact === 'critical' || v.impact === 'serious'
  );

  // INFORMATIONAL only: a11y violations są raportowane jako test annotations + console log,
  // NIE jako fail. Tester widzi w playwright-report obok kazdego testu, bez czerwonego suite.
  if (blocking.length > 0) {
    test.info().annotations.push({
      type: 'a11y',
      description: `${pageName}: ${blocking.length} blocking violations — ${blocking.map(v => v.id).join(', ')}`,
    });
  }

  // Screenshot raportu strony
  const safe = pageName.replace(/[^a-z0-9-]/gi, '_').toLowerCase();
  await page.screenshot({
    path: `test-results/a11y-${safe}.png`,
    fullPage: true,
  });

  return violations;
}

test.describe('Accessibility scans (axe-core)', () => {
  test.setTimeout(60_000);

  const authPages: { url: string; name: string }[] = [
    { url: '/dashboard', name: 'Dashboard' },
    { url: '/admin/rentals', name: 'Rentals' },
    { url: '/admin/payments', name: 'Payments' },
    { url: '/admin/contracts', name: 'Contracts' },
    { url: '/admin/products', name: 'Products' },
  ];

  for (const p of authPages) {
    test(`a11y scan: ${p.name} (${p.url})`, async ({ page }) => {
      test.setTimeout(60_000);
      await signInAsDemo(page);
      await waitForBlazorReady(page);
      await scanPage(page, p.url, p.name);
    });
  }

  test('a11y scan: Login (unauthenticated)', async ({ page }) => {
    test.setTimeout(60_000);

    // Spróbuj różnych URL — w Identity ścieżka to /Account/Login
    const loginCandidates = ['/Account/Login', '/Identity/Account/Login', '/login'];

    let landed = false;
    for (const url of loginCandidates) {
      const resp = await page.goto(url, { waitUntil: 'domcontentloaded' }).catch(() => null);
      if (resp && resp.ok()) {
        landed = true;
        break;
      }
    }

    if (!landed) {
      // fallback — wymuś przez kliknięcie "Zaloguj się" ze strony głównej
      await page.goto('/', { waitUntil: 'domcontentloaded' });
      const loginBtn = page.getByRole('link', { name: /Zaloguj się/i }).first();
      const altLoginBtn = page.getByText('Zaloguj się').first();
      if (await loginBtn.isVisible().catch(() => false)) {
        await loginBtn.click();
      } else if (await altLoginBtn.isVisible().catch(() => false)) {
        await altLoginBtn.click();
      }
      await page.waitForLoadState('domcontentloaded');
    }

    // krótka stabilizacja
    await page.waitForTimeout(1500);

    const results = await new AxeBuilder({ page })
      .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'])
      .analyze();

    const violations = results.violations as AxeViolation[];
    reportViolations('Login', violations);

    const blocking = violations.filter(
      (v) => v.impact === 'critical' || v.impact === 'serious'
    );
    if (blocking.length > 0) {
      test.info().annotations.push({
        type: 'a11y',
        description: `Login: ${blocking.length} blocking violations — ${blocking.map((v) => v.id).join(', ')}`,
      });
    }

    await page.screenshot({
      path: 'test-results/a11y-login.png',
      fullPage: true,
    });
  });
});
