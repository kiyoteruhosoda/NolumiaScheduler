import { expect, test } from '@playwright/test';

const appUrl = process.env.UI_TEST_APP_URL;

test.describe('Calendar display mode switching (real app)', () => {
  test('week view shows the main layers and mode switching works', async ({ page }) => {
    test.skip(!appUrl, 'Skipping the real-app UI test because UI_TEST_APP_URL is not set');

    await page.goto(appUrl!);

    await page.getByText('週表示').click();

    await expect(page.getByText('終日')).toBeVisible();
    await expect(page.getByText('00:00')).toBeVisible();

    // An event element in the real app (including its time label) and a click on it
    const eventLike = page.locator('text=/\d{1,2}:\d{2}\s*[–-]\s*\d{1,2}:\d{2}/').first();
    if (await eventLike.count() > 0) {
      await eventLike.click();
      await expect(eventLike).toBeVisible();
    }
  });
});
