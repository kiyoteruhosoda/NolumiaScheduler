import { defineConfig, devices } from '@playwright/test';

export default defineConfig({
  testDir: './ui-tests',
  fullyParallel: true,
  retries: 0,
  use: {
    trace: 'on-first-retry'
  },
  projects: [
    {
      name: 'webkit',
      use: { ...devices['Desktop Safari'] }
    }
  ]
});
