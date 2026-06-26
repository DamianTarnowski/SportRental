import { defineConfig, devices } from '@playwright/test';

/**
 * Playwright config — testy idą przeciw srental2.azurewebsites.net (live prod, demo isolated tenant per session, 8h TTL).
 * Per memory: dane demo usuwa się po 8h więc bez sprzątania na końcu.
 */
const BASE_URL = process.env.E2E_BASE_URL ?? 'https://srental2.azurewebsites.net';

export default defineConfig({
  testDir: './specs',
  outputDir: './test-results',
  fullyParallel: false, // demo sesja per browser — sekwencyjnie żeby nie bić tego samego tenanta
  retries: process.env.CI ? 1 : 0,
  workers: 1, // single worker bo każdy test signinuje demo

  reporter: [
    ['html', { outputFolder: './playwright-report', open: 'never' }],
    ['list'],
    ['json', { outputFile: './test-results/results.json' }],
  ],

  use: {
    baseURL: BASE_URL,
    headless: true,
    ignoreHTTPSErrors: true,
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',
    trace: 'retain-on-failure',
    actionTimeout: 15_000,
    navigationTimeout: 30_000,
    locale: 'pl-PL',
    timezoneId: 'Europe/Warsaw',
  },

  projects: [
    {
      name: 'desktop',
      use: { ...devices['Desktop Chrome'], viewport: { width: 1440, height: 900 } },
    },
    {
      name: 'mobile',
      use: { ...devices['iPhone 14'] },
    },
    // Cross-browser matrix (uruchamiane na R620 gdzie wszystkie 3 silniki są zainstalowane)
    {
      name: 'desktop-chromium',
      use: { ...devices['Desktop Chrome'], viewport: { width: 1440, height: 900 } },
      testMatch: /cross-browser\.spec\.ts/,
    },
    {
      name: 'desktop-firefox',
      use: { ...devices['Desktop Firefox'], viewport: { width: 1440, height: 900 } },
      testMatch: /cross-browser\.spec\.ts/,
    },
    {
      name: 'desktop-webkit',
      use: { ...devices['Desktop Safari'], viewport: { width: 1440, height: 900 } },
      testMatch: /cross-browser\.spec\.ts/,
    },
  ],

  expect: {
    timeout: 10_000,
  },
});
