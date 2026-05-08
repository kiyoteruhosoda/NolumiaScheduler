import type { Locator, Page } from '@playwright/test';

export interface CalendarDisplayMode {
  readonly modeName: string;
  activate(page: Page): Promise<void>;
}

abstract class BaseDisplayMode implements CalendarDisplayMode {
  protected constructor(
    public readonly modeName: string,
    private readonly modeButtonTestId: string
  ) {}

  async activate(page: Page): Promise<void> {
    await page.getByTestId(this.modeButtonTestId).click();
  }
}

export class WeekDisplayMode extends BaseDisplayMode {
  constructor() {
    super('週表示', 'mode-week');
  }
}

export class MonthDisplayMode extends BaseDisplayMode {
  constructor() {
    super('月表示', 'mode-month');
  }
}

export interface WeekScheduleExpectation {
  readonly label: string;
  readonly dayColumn: string;
  readonly startTime: string;
  readonly endTime: string;
}

export class StandardWeekScheduleExpectation implements WeekScheduleExpectation {
  constructor(
    public readonly label: string,
    public readonly dayColumn: string,
    public readonly startTime: string,
    public readonly endTime: string
  ) {}
}

export class CalendarModeContext {
  constructor(private readonly currentModeLabel: Locator) {}

  async switchTo(mode: CalendarDisplayMode, page: Page): Promise<void> {
    await mode.activate(page);
    await this.currentModeLabel.waitFor({ state: 'visible' });
  }

  expectedText(mode: CalendarDisplayMode): string {
    return mode.modeName;
  }
}
