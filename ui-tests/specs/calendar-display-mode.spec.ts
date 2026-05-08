import { expect, test } from '@playwright/test';

const appUrl = process.env.UI_TEST_APP_URL;

test.describe('Calendar display mode switching (real app)', () => {
  test('週表示で主要レイヤーが表示され、モード切替が動作する', async ({ page }) => {
    test.skip(!appUrl, 'UI_TEST_APP_URL が未設定のため実アプリUIテストをスキップします');

    await page.goto(appUrl!);

    await page.getByText('週表示').click();

    await expect(page.getByText('終日')).toBeVisible();
    await expect(page.getByText('00:00')).toBeVisible();

    // 実アプリ上の予定要素（時間ラベルを含む）とクリック操作
    const eventLike = page.locator('text=/\d{1,2}:\d{2}\s*[–-]\s*\d{1,2}:\d{2}/').first();
    if (await eventLike.count() > 0) {
      await eventLike.click();
      await expect(eventLike).toBeVisible();
    }
  });
});
