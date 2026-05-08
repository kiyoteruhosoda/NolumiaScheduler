import { expect, test } from '@playwright/test';
import {
  CalendarModeContext,
  MonthDisplayMode,
  StandardWeekScheduleExpectation,
  WeekDisplayMode,
  type CalendarDisplayMode,
  type WeekScheduleExpectation
} from '../support/calendarModes';

const calendarPageMarkup = `
<!doctype html>
<html lang="ja">
<body>
  <main>
    <h1 data-testid="calendar-title">Nolumia Scheduler</h1>
    <button data-testid="mode-week">週表示</button>
    <button data-testid="mode-month">月表示</button>
    <p data-testid="current-mode">未選択</p>

    <section data-testid="week-schedule" hidden>
      <h2>第3週</h2>
      <article data-testid="week-event"
               data-day="月"
               data-start="10:00"
               data-end="11:00">
        定例1on1
      </article>
    </section>

    <script>
      const mode = document.querySelector('[data-testid="current-mode"]');
      const weekSchedule = document.querySelector('[data-testid="week-schedule"]');
      document.querySelector('[data-testid="mode-week"]').addEventListener('click', () => {
        mode.textContent = '週表示';
        weekSchedule.hidden = false;
      });
      document.querySelector('[data-testid="mode-month"]').addEventListener('click', () => {
        mode.textContent = '月表示';
        weekSchedule.hidden = true;
      });
    </script>
  </main>
</body>
</html>`;


test.describe('Calendar display mode switching', () => {
  test('表示モードのユースケースをポリモーフィズムで再利用検証する', async ({ page }) => {
    await page.setContent(calendarPageMarkup);

    const context = new CalendarModeContext(page.getByTestId('current-mode'));
    const modes: CalendarDisplayMode[] = [new WeekDisplayMode(), new MonthDisplayMode()];

    for (const mode of modes) {
      await context.switchTo(mode, page);
      await expect(page.getByTestId('current-mode')).toHaveText(context.expectedText(mode));
    }
  });

  test('週表示で週スケジュールが正しく描画される', async ({ page }) => {
    await page.setContent(calendarPageMarkup);

    const context = new CalendarModeContext(page.getByTestId('current-mode'));
    await context.switchTo(new WeekDisplayMode(), page);

    const expected = new StandardWeekScheduleExpectation('定例1on1', '月', '10:00', '11:00');
    const weekSchedule = page.getByTestId('week-schedule');
    const weekEvent = page.getByTestId('week-event');

    await expect(weekSchedule).toBeVisible();
    await expect(weekEvent).toHaveText(expected.label);
    await expect(weekEvent).toHaveAttribute('data-day', expected.dayColumn);
    await expect(weekEvent).toHaveAttribute('data-start', expected.startTime);
    await expect(weekEvent).toHaveAttribute('data-end', expected.endTime);
  });
});
