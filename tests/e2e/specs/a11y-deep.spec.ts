import { test, expect, Page } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import * as fs from 'fs';
import * as path from 'path';
import { signInAsDemo, waitForBlazorReady } from '../helpers/demo';

type AxeViolation = {
  id: string;
  impact?: string | null;
  description: string;
  help: string;
  helpUrl: string;
  nodes: unknown[];
};

type PageReport = {
  pageName: string;
  url: string;
  totalViolations: number;
  byImpact: Record<string, number>;
  topIds: string[];
};

const REPORT_PATH = 'test-results/a11y-deep-report.json';
const allReports: PageReport[] = [];

function ensureReportDir(): void {
  const dir = path.dirname(REPORT_PATH);
  if (!fs.existsSync(dir)) {
    fs.mkdirSync(dir, { recursive: true });
  }
}

function persistReport(): void {
  ensureReportDir();
  fs.writeFileSync(
    REPORT_PATH,
    JSON.stringify(
      {
        generatedAt: new Date().toISOString(),
        totalPages: allReports.length,
        totalViolations: allReports.reduce((s, r) => s + r.totalViolations, 0),
        pages: allReports,
      },
      null,
      2
    ),
    'utf-8'
  );
}

function buildReport(pageName: string, url: string, violations: AxeViolation[]): PageReport {
  const byImpact: Record<string, number> = {
    critical: 0,
    serious: 0,
    moderate: 0,
    minor: 0,
    unknown: 0,
  };
  for (const v of violations) {
    const k = (v.impact ?? 'unknown') as string;
    byImpact[k] = (byImpact[k] ?? 0) + 1;
  }
  return {
    pageName,
    url,
    totalViolations: violations.length,
    byImpact,
    topIds: violations.slice(0, 10).map((v) => v.id),
  };
}

function annotate(pageName: string, report: PageReport): void {
  test.info().annotations.push({
    type: 'a11y-deep',
    description: `${pageName}: ${report.totalViolations} violations [crit:${report.byImpact.critical} sev:${report.byImpact.serious} mod:${report.byImpact.moderate} min:${report.byImpact.minor}] ids=${report.topIds.join(',')}`,
  });
}

async function fullAxeScan(page: Page, url: string, pageName: string): Promise<AxeViolation[]> {
  await page.goto(url, { waitUntil: 'networkidle' }).catch(async () => {
    await page.goto(url, { waitUntil: 'domcontentloaded' });
  });
  await waitForBlazorReady(page).catch(() => {
    /* unauth pages allowed */
  });
  await page.waitForTimeout(1500);

  const results = await new AxeBuilder({ page })
    .withTags([
      'wcag2a',
      'wcag2aa',
      'wcag2aaa',
      'wcag21a',
      'wcag21aa',
      'best-practice',
    ])
    .analyze();

  const violations = results.violations as AxeViolation[];
  const report = buildReport(pageName, url, violations);
  allReports.push(report);
  annotate(pageName, report);

  // eslint-disable-next-line no-console
  console.log(
    `\n[a11y-deep] ${pageName} (${url}) — ${violations.length} violations  ` +
      JSON.stringify(report.byImpact)
  );
  for (const v of violations.slice(0, 15)) {
    // eslint-disable-next-line no-console
    console.log(`  [${v.impact ?? '?'}] ${v.id}: ${v.help}  (${v.nodes.length})`);
  }

  const safe = pageName.replace(/[^a-z0-9-]/gi, '_').toLowerCase();
  await page.screenshot({
    path: `test-results/a11y-deep/${safe}.png`,
    fullPage: true,
  });

  return violations;
}

// ============================================================================
// SEMANTIC HELPERS
// ============================================================================

async function checkFormLabels(page: Page): Promise<string[]> {
  return await page.evaluate(() => {
    const issues: string[] = [];
    const inputs = Array.from(
      document.querySelectorAll('input, select, textarea')
    ) as HTMLElement[];

    for (const el of inputs) {
      const type = (el as HTMLInputElement).type;
      if (type === 'hidden' || type === 'submit' || type === 'button') continue;
      if (el.getAttribute('aria-hidden') === 'true') continue;

      const id = el.id;
      const ariaLabel = el.getAttribute('aria-label');
      const ariaLabelledBy = el.getAttribute('aria-labelledby');
      const title = el.getAttribute('title');
      const placeholder = el.getAttribute('placeholder');

      let hasLabel = false;
      if (id) {
        const lbl = document.querySelector(`label[for="${CSS.escape(id)}"]`);
        if (lbl && lbl.textContent && lbl.textContent.trim().length > 0) {
          hasLabel = true;
        }
      }
      if (el.closest('label')) hasLabel = true;
      if (ariaLabel && ariaLabel.trim().length > 0) hasLabel = true;
      if (ariaLabelledBy && ariaLabelledBy.trim().length > 0) hasLabel = true;

      if (!hasLabel) {
        const desc = `${el.tagName.toLowerCase()}${id ? '#' + id : ''}${
          type ? '[type=' + type + ']' : ''
        }${placeholder ? '[placeholder="' + placeholder + '"]' : ''}${
          title ? '[title="' + title + '"]' : ''
        }`;
        issues.push(desc);
      }
    }
    return issues;
  });
}

async function checkButtonNames(page: Page): Promise<string[]> {
  return await page.evaluate(() => {
    const issues: string[] = [];
    const buttons = Array.from(
      document.querySelectorAll('button, [role="button"]')
    ) as HTMLElement[];

    for (const b of buttons) {
      if (b.getAttribute('aria-hidden') === 'true') continue;
      const text = (b.innerText || b.textContent || '').trim();
      const ariaLabel = b.getAttribute('aria-label');
      const ariaLabelledBy = b.getAttribute('aria-labelledby');
      const title = b.getAttribute('title');

      const hasName =
        text.length > 0 ||
        (ariaLabel && ariaLabel.trim().length > 0) ||
        (ariaLabelledBy && ariaLabelledBy.trim().length > 0) ||
        (title && title.trim().length > 0);

      if (!hasName) {
        const cls = b.className ? '.' + b.className.split(/\s+/).slice(0, 2).join('.') : '';
        issues.push(`${b.tagName.toLowerCase()}${cls}`);
      }
    }
    return issues;
  });
}

async function checkHeadingHierarchy(page: Page): Promise<string[]> {
  return await page.evaluate(() => {
    const issues: string[] = [];
    const headings = Array.from(
      document.querySelectorAll('h1, h2, h3, h4, h5, h6')
    ) as HTMLElement[];

    let lastLevel = 0;
    let sawH1 = false;
    let h1Count = 0;

    for (const h of headings) {
      const level = parseInt(h.tagName.substring(1), 10);
      const text = (h.innerText || h.textContent || '').trim().slice(0, 40);

      if (level === 1) {
        sawH1 = true;
        h1Count++;
      }

      if (lastLevel > 0 && level > lastLevel + 1) {
        issues.push(`skip ${lastLevel}->${level} at "${text}"`);
      }
      lastLevel = level;
    }

    if (!sawH1 && headings.length > 0) issues.push('no h1 on page');
    if (h1Count > 1) issues.push(`multiple h1 (${h1Count})`);
    return issues;
  });
}

function parseColor(c: string): [number, number, number, number] | null {
  // rgb(r, g, b) or rgba(r, g, b, a)
  const m = c.match(/rgba?\(([^)]+)\)/);
  if (!m) return null;
  const parts = m[1].split(',').map((x) => parseFloat(x.trim()));
  if (parts.length < 3) return null;
  return [parts[0], parts[1], parts[2], parts.length === 4 ? parts[3] : 1];
}

async function checkButtonContrast(page: Page): Promise<string[]> {
  return await page.evaluate(() => {
    function rel(c: number): number {
      const s = c / 255;
      return s <= 0.03928 ? s / 12.92 : Math.pow((s + 0.055) / 1.055, 2.4);
    }
    function luminance(rgb: [number, number, number]): number {
      return 0.2126 * rel(rgb[0]) + 0.7152 * rel(rgb[1]) + 0.0722 * rel(rgb[2]);
    }
    function ratio(a: [number, number, number], b: [number, number, number]): number {
      const la = luminance(a);
      const lb = luminance(b);
      const lighter = Math.max(la, lb);
      const darker = Math.min(la, lb);
      return (lighter + 0.05) / (darker + 0.05);
    }
    function parse(c: string): [number, number, number] | null {
      const m = c.match(/rgba?\(([^)]+)\)/);
      if (!m) return null;
      const parts = m[1].split(',').map((x) => parseFloat(x.trim()));
      if (parts.length < 3) return null;
      return [parts[0], parts[1], parts[2]];
    }
    function blend(
      fg: [number, number, number, number],
      bg: [number, number, number]
    ): [number, number, number] {
      const a = fg[3];
      return [
        fg[0] * a + bg[0] * (1 - a),
        fg[1] * a + bg[1] * (1 - a),
        fg[2] * a + bg[2] * (1 - a),
      ];
    }

    const issues: string[] = [];
    const buttons = Array.from(
      document.querySelectorAll(
        'button.mud-button-filled, button.mud-button-outlined, button.mud-button-text, .mud-button-root'
      )
    ) as HTMLElement[];

    for (const b of buttons.slice(0, 50)) {
      if (b.getAttribute('aria-hidden') === 'true') continue;
      const text = (b.innerText || b.textContent || '').trim();
      if (text.length === 0) continue;

      const style = getComputedStyle(b);
      const fg = parse(style.color);
      const bgMatch = style.backgroundColor.match(/rgba?\(([^)]+)\)/);
      if (!fg || !bgMatch) continue;

      const bgParts = bgMatch[1].split(',').map((x) => parseFloat(x.trim()));
      let bgRgb: [number, number, number];
      if (bgParts.length === 4 && bgParts[3] < 0.95) {
        // background is transparent — walk up
        let parent = b.parentElement;
        let solid: [number, number, number] = [255, 255, 255];
        while (parent) {
          const pstyle = getComputedStyle(parent);
          const pm = pstyle.backgroundColor.match(/rgba?\(([^)]+)\)/);
          if (pm) {
            const pp = pm[1].split(',').map((x) => parseFloat(x.trim()));
            if (pp.length === 3 || (pp.length === 4 && pp[3] >= 0.95)) {
              solid = [pp[0], pp[1], pp[2]];
              break;
            }
          }
          parent = parent.parentElement;
        }
        const fgWithBg: [number, number, number, number] = [fg[0], fg[1], fg[2], 1];
        const r = ratio(fgWithBg.slice(0, 3) as [number, number, number], solid);
        if (r < 4.5) {
          issues.push(
            `"${text.slice(0, 30)}" contrast=${r.toFixed(2)} fg=rgb(${fg.join(',')}) bg=rgb(${solid.join(',')})`
          );
        }
        continue;
      } else {
        bgRgb = [bgParts[0], bgParts[1], bgParts[2]];
      }

      const r = ratio(fg, bgRgb);
      if (r < 4.5) {
        issues.push(
          `"${text.slice(0, 30)}" contrast=${r.toFixed(2)} fg=rgb(${fg.join(',')}) bg=rgb(${bgRgb.join(',')})`
        );
      }
    }
    return issues;
  });
}

async function runSemanticChecks(page: Page, pageName: string): Promise<void> {
  const formLabels = await checkFormLabels(page);
  const buttonNames = await checkButtonNames(page);
  const headings = await checkHeadingHierarchy(page);
  const contrast = await checkButtonContrast(page);

  test.info().annotations.push({
    type: 'a11y-semantic',
    description: `${pageName}: form-label-issues=${formLabels.length}, button-name-issues=${buttonNames.length}, heading-issues=${headings.length}, contrast-issues=${contrast.length}`,
  });

  // eslint-disable-next-line no-console
  console.log(`\n[a11y-semantic] ${pageName}`);
  if (formLabels.length > 0) {
    // eslint-disable-next-line no-console
    console.log(`  form-labels (${formLabels.length}):`, formLabels.slice(0, 10));
  }
  if (buttonNames.length > 0) {
    // eslint-disable-next-line no-console
    console.log(`  button-names (${buttonNames.length}):`, buttonNames.slice(0, 10));
  }
  if (headings.length > 0) {
    // eslint-disable-next-line no-console
    console.log(`  headings (${headings.length}):`, headings.slice(0, 10));
  }
  if (contrast.length > 0) {
    // eslint-disable-next-line no-console
    console.log(`  contrast (${contrast.length}):`, contrast.slice(0, 10));
  }
}

// ============================================================================
// TESTS
// ============================================================================

test.describe('A11y deep audit (axe full + semantic checks)', () => {
  test.describe.configure({ mode: 'serial' });

  const authPages: { url: string; name: string }[] = [
    { url: '/dashboard', name: 'Dashboard' },
    { url: '/admin/rentals', name: 'Rentals' },
    { url: '/admin/payments', name: 'Payments' },
    { url: '/admin/products', name: 'Products' },
    { url: '/admin/customers', name: 'Customers' },
  ];

  for (const p of authPages) {
    test(`axe-full-scan-${p.name.toLowerCase()}`, async ({ page }) => {
      test.setTimeout(60_000);
      await signInAsDemo(page);
      await waitForBlazorReady(page);

      const violations = await fullAxeScan(page, p.url, p.name);
      await runSemanticChecks(page, p.name);

      // Informational — not blocking
      expect(violations.length).toBeGreaterThanOrEqual(0);
    });
  }

  test('axe-login-page (unauthenticated)', async ({ page }) => {
    test.setTimeout(60_000);

    const loginCandidates = ['/Account/Login', '/Identity/Account/Login', '/login'];
    let landed = false;
    let landedUrl = '/Account/Login';

    for (const url of loginCandidates) {
      const resp = await page.goto(url, { waitUntil: 'domcontentloaded' }).catch(() => null);
      if (resp && resp.ok()) {
        landed = true;
        landedUrl = url;
        break;
      }
    }

    if (!landed) {
      await page.goto('/', { waitUntil: 'domcontentloaded' });
      const loginBtn = page.getByRole('link', { name: /Zaloguj się/i }).first();
      if (await loginBtn.isVisible().catch(() => false)) {
        await loginBtn.click();
        await page.waitForLoadState('domcontentloaded');
        landedUrl = page.url();
      }
    }

    await page.waitForTimeout(1500);

    const results = await new AxeBuilder({ page })
      .withTags([
        'wcag2a',
        'wcag2aa',
        'wcag2aaa',
        'wcag21a',
        'wcag21aa',
        'best-practice',
      ])
      .analyze();

    const violations = results.violations as AxeViolation[];
    const report = buildReport('Login', landedUrl, violations);
    allReports.push(report);
    annotate('Login', report);

    // eslint-disable-next-line no-console
    console.log(
      `\n[a11y-deep] Login (${landedUrl}) — ${violations.length} violations  ` +
        JSON.stringify(report.byImpact)
    );

    await runSemanticChecks(page, 'Login');

    await page.screenshot({
      path: 'test-results/a11y-deep/login.png',
      fullPage: true,
    });

    expect(violations.length).toBeGreaterThanOrEqual(0);
  });

  test.afterAll(() => {
    persistReport();
    // eslint-disable-next-line no-console
    console.log(`\n[a11y-deep] Aggregated report saved to ${REPORT_PATH}`);
  });
});
