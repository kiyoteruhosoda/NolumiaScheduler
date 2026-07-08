import { expect, test } from '@playwright/test';

const appUrl = process.env.UI_TEST_APP_URL;

test.describe('Business Calendar Edit – Shift on only holidays (real app)', () => {
  test.beforeEach(async ({ page }) => {
    test.skip(!appUrl, 'UI_TEST_APP_URL が未設定のため実アプリUIテストをスキップします');
    await page.goto(appUrl!);
  });

  test('ビジネスカレンダー新規作成画面に「休日のみシフト」チェックボックスが表示される', async ({ page }) => {
    // Navigate to Business Calendars tab
    await page.getByText('Business Calendars').click();

    // Click "New Calendar" to open the edit form
    await page.getByText('+ New Calendar').click();

    // The "Shift only on holidays" checkbox must be visible
    const checkbox = page.getByRole('checkbox', { name: /shift only on holidays|休日のみシフト/i });
    await expect(checkbox).toBeVisible();
  });

  test('「休日のみシフト」チェックボックスのON/OFFが切り替えられる', async ({ page }) => {
    await page.getByText('Business Calendars').click();
    await page.getByText('+ New Calendar').click();

    const checkbox = page.getByRole('checkbox', { name: /shift only on holidays|休日のみシフト/i });
    await expect(checkbox).toBeVisible();

    // Initially unchecked
    await expect(checkbox).not.toBeChecked();

    // Check it
    await checkbox.check();
    await expect(checkbox).toBeChecked();

    // Uncheck it
    await checkbox.uncheck();
    await expect(checkbox).not.toBeChecked();
  });

  test('「休日のみシフト」をONにして保存するとカレンダーに反映される', async ({ page }) => {
    await page.getByText('Business Calendars').click();
    await page.getByText('+ New Calendar').click();

    // Fill in calendar name
    const nameBox = page.getByPlaceholder(/calendar name/i);
    await nameBox.fill('E2E Test Calendar');

    // Enable "Shift only on holidays"
    const checkbox = page.getByRole('checkbox', { name: /shift only on holidays|休日のみシフト/i });
    await checkbox.check();
    await expect(checkbox).toBeChecked();

    // Save
    await page.getByRole('button', { name: /^save$/i }).click();

    // After save we should be back on the list; find the new calendar and edit it
    await page.getByText('E2E Test Calendar').click();

    // Verify the checkbox is still checked after reload
    const checkboxAfterReload = page.getByRole('checkbox', { name: /shift only on holidays|休日のみシフト/i });
    await expect(checkboxAfterReload).toBeChecked();

    // Cleanup: delete the created calendar
    await page.getByRole('button', { name: /delete calendar/i }).click();
  });
});
