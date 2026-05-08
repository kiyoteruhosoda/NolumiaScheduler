import { expect, test } from '@playwright/test';

const markup = `<!doctype html><html lang="ja"><body>
<button data-testid="mode-week">週表示</button><button data-testid="mode-month">月表示</button>
<section data-testid="week" hidden>
  <div data-testid="all-day-lane">終日</div>
  <div data-testid="time-labels">00:00 01:00 ... 23:00</div>
  <div data-testid="week-grid" data-columns="7" data-scroll="vertical"></div>
  <article data-testid="event" data-top="570" data-height="60">定例1on1</article>
</section>
<section data-testid="month"></section>
<script>
const w=document.querySelector('[data-testid=week]'); const m=document.querySelector('[data-testid=month]');
document.querySelector('[data-testid=mode-week]').onclick=()=>{w.hidden=false;m.hidden=true;};
document.querySelector('[data-testid=mode-month]').onclick=()=>{w.hidden=true;m.hidden=false;};
</script></body></html>`;

test('週表示グリッドは7列・縦スクロール・終日レーン分離を満たす', async ({ page }) => {
  await page.setContent(markup);
  await page.getByTestId('mode-week').click();
  await expect(page.getByTestId('week')).toBeVisible();
  await expect(page.getByTestId('week-grid')).toHaveAttribute('data-columns', '7');
  await expect(page.getByTestId('week-grid')).toHaveAttribute('data-scroll', 'vertical');
  await expect(page.getByTestId('all-day-lane')).toBeVisible();
  await expect(page.getByTestId('event')).toHaveAttribute('data-top', '570');
  await expect(page.getByTestId('event')).toHaveAttribute('data-height', '60');
});
