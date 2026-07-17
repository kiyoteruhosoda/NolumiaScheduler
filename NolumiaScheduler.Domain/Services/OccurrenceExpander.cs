using NolumiaScheduler.Domain.Aggregates;
using NolumiaScheduler.Domain.Entities;
using NolumiaScheduler.Domain.ValueObjects;

namespace NolumiaScheduler.Domain.Services;

public class OccurrenceExpander(IBusinessDayShiftService shiftService) : IOccurrenceExpander
{
    private readonly IBusinessDayShiftService _shiftService = shiftService;

    public IReadOnlyList<EventOccurrence> Expand(
        CalendarEvent calendarEvent,
        LocalDateValue fromDate,
        LocalDateValue toDate,
        BusinessCalendar? businessCalendar)
    {
        if (calendarEvent.IsSingle())
            return ExpandSingle(calendarEvent, fromDate, toDate);

        return ExpandRecurring(calendarEvent, fromDate, toDate, businessCalendar);
    }

    private static IReadOnlyList<EventOccurrence> ExpandSingle(
        CalendarEvent ev, LocalDateValue fromDate, LocalDateValue toDate)
    {
        var schedule = ev.SingleSchedule!;
        var tz = ev.TimeZoneId.ToTimeZoneInfo();
        var startDate = LocalSchedulePoint.LocalDateOf(schedule.StartUtc, tz);

        if (startDate < fromDate || startDate > toDate)
            return [];

        return
        [
            new EventOccurrence(
                ev.Id, startDate,
                LocalSchedulePoint.LocalTimeOf(schedule.StartUtc, tz), schedule.DurationMinutes,
                ev.Title, ev.Location, ev.Visibility,
                colorKey: ev.ColorKey)
        ];
    }

    private List<EventOccurrence> ExpandRecurring(
        CalendarEvent ev, LocalDateValue fromDate, LocalDateValue toDate,
        BusinessCalendar? businessCalendar)
    {
        var schedule = ev.RecurringSchedule!;
        var rule = schedule.RecurrenceRule;
        var tz = ev.TimeZoneId.ToTimeZoneInfo();
        var results = new List<EventOccurrence>();

        // The anchor's local date/time-of-day in the event timezone. The time-of-day is the
        // nominal series start used for stable occurrence keys; each occurrence's actual local
        // start is the UTC-pinned instant converted back to local (it may drift across DST).
        var anchorLocalDate = LocalSchedulePoint.LocalDateOf(schedule.AnchorUtc, tz);
        var anchorLocalTime = LocalSchedulePoint.LocalTimeOf(schedule.AnchorUtc, tz);

        // The UTC-pinned local start time-of-day on a given local date (docs/time-model.md §4-2).
        LocalTimeValue SeriesTimeAt(LocalDateValue date)
        {
            var days = date.ToDateOnly().DayNumber - anchorLocalDate.ToDateOnly().DayNumber;
            return LocalSchedulePoint.LocalTimeOf(schedule.AnchorUtc.AddDays(days), tz);
        }

        // A business-day adjustment can shift a candidate date by several calendar days, so a
        // candidate whose raw date sits just outside [fromDate, toDate] can still land inside the
        // window once shifted. Generate candidates over a widened upper bound and let the exact
        // post-shift filter below decide membership — otherwise narrow windows (alarms, the week
        // view) would drop, e.g., a "3 business days before month-end" occurrence whose month-end
        // candidate falls after the window end. The margin matches the coarse pre-filter's, which
        // is the largest shift the stored active span can represent.
        var candidateBound = rule.Adjustment != null
            ? toDate.AddDays(CalendarEvent.PeriodOverlapMarginDays)
            : toDate;
        var candidateDates = GenerateCandidateDates(anchorLocalDate, rule, candidateBound);

        foreach (var candidateDate in candidateDates)
        {
            var key = new OccurrenceLocalKey(candidateDate, anchorLocalTime);

            // Check skip
            var exception = ev.Exceptions.FirstOrDefault(e => e.OccurrenceKey.Equals(key));
            if (exception != null && exception.Type == ExceptionType.Skip)
                continue;

            // Check move before the range filter: an occurrence moved into the requested window
            // must appear even when its original candidate date lies outside the window.
            var move = ev.Moves.FirstOrDefault(m => m.OccurrenceKey.Equals(key));
            if (move != null)
            {
                if (move.NewDate >= fromDate && move.NewDate <= toDate)
                {
                    // A move may coexist with a content override (editing then relocating the
                    // same occurrence). Fall back to the override's content/time before the
                    // series defaults so the customization survives the move.
                    var movedOverride = exception is { Type: ExceptionType.Override } ? exception.Override : null;
                    results.Add(new EventOccurrence(
                        ev.Id, move.NewDate,
                        move.NewStartTime ?? movedOverride?.StartTime ?? SeriesTimeAt(candidateDate),
                        move.NewDurationMinutes ?? movedOverride?.DurationMinutes ?? schedule.DurationMinutes,
                        move.Title ?? movedOverride?.Title ?? ev.Title,
                        move.Location ?? movedOverride?.Location ?? ev.Location,
                        move.Visibility ?? movedOverride?.Visibility ?? ev.Visibility,
                        isMoved: true,
                        seriesKey: key,
                        colorKey: ev.ColorKey,
                        alarmEnabled: movedOverride?.AlarmEnabled ?? true));
                }
                continue;
            }

            var adjustedDate = candidateDate;

            if (rule.Adjustment != null && businessCalendar != null)
            {
                if (rule.Adjustment.Cancels(candidateDate, businessCalendar))
                    continue;

                adjustedDate = _shiftService.Shift(candidateDate, rule.Adjustment, businessCalendar);
            }

            if (adjustedDate < fromDate || adjustedDate > toDate)
                continue;

            // Check override
            if (exception != null && exception.Type == ExceptionType.Override && exception.Override != null)
            {
                var ov = exception.Override;
                results.Add(new EventOccurrence(
                    ev.Id, adjustedDate,
                    ov.StartTime ?? SeriesTimeAt(adjustedDate),
                    ov.DurationMinutes ?? schedule.DurationMinutes,
                    ov.Title ?? ev.Title,
                    ov.Location ?? ev.Location,
                    ov.Visibility ?? ev.Visibility,
                    isOverridden: true,
                    seriesKey: key,
                    colorKey: ev.ColorKey,
                    alarmEnabled: ov.AlarmEnabled ?? true));
                continue;
            }

            results.Add(new EventOccurrence(
                ev.Id, adjustedDate,
                SeriesTimeAt(adjustedDate), schedule.DurationMinutes,
                ev.Title, ev.Location, ev.Visibility,
                seriesKey: key,
                colorKey: ev.ColorKey));
        }

        return results;
    }

    private static IEnumerable<LocalDateValue> GenerateCandidateDates(
        LocalDateValue startDate, RecurrenceRule rule, LocalDateValue endBound)
    {
        var endDate = rule.EndDate < endBound ? rule.EndDate : endBound;

        return rule.RuleType switch
        {
            RecurrenceType.Weekly => GenerateWeekly(startDate, rule.Interval, rule.Weekly!, endDate),
            RecurrenceType.Monthly => GenerateMonthly(startDate, rule.Interval, rule.Monthly!, endDate),
            RecurrenceType.Yearly => GenerateYearly(startDate, rule.Interval, rule.Yearly!, endDate),
            _ => [],
        };
    }

    private static IEnumerable<LocalDateValue> GenerateWeekly(
        LocalDateValue startDate, int interval, WeeklyRule weekly, LocalDateValue endDate)
    {
        var current = startDate.ToDateOnly();
        var end = endDate.ToDateOnly();

        // Align to start of week (Monday)
        var weekStart = current.AddDays(-(((int)current.DayOfWeek + 6) % 7));

        while (weekStart <= end)
        {
            foreach (var weekday in weekly.Weekdays)
            {
                var dayOffset = ((int)weekday.ToDayOfWeek() - (int)DayOfWeek.Monday + 7) % 7;
                var candidateDayNumber = (long)weekStart.DayNumber + dayOffset;
                // The last week of the calendar (year 9999) can extend past DateOnly.MaxValue.
                if (candidateDayNumber > DateOnly.MaxValue.DayNumber) yield break;
                var candidate = DateOnly.FromDayNumber((int)candidateDayNumber);

                if (candidate < startDate.ToDateOnly()) continue;
                if (candidate > end) continue;

                yield return LocalDateValue.FromDateOnly(candidate);
            }

            var nextWeekDayNumber = (long)weekStart.DayNumber + 7L * interval;
            if (nextWeekDayNumber > DateOnly.MaxValue.DayNumber) yield break;
            weekStart = DateOnly.FromDayNumber((int)nextWeekDayNumber);
        }
    }

    private static IEnumerable<LocalDateValue> GenerateMonthly(
        LocalDateValue startDate, int interval, MonthlyRule monthly, LocalDateValue endDate)
    {
        var current = new DateOnly(startDate.Year, startDate.Month, 1);
        var end = endDate.ToDateOnly();

        while (true)
        {
            var candidate = ResolveMonthlyDate(current.Year, current.Month, monthly);
            if (candidate != null)
            {
                if (candidate.Value > end) yield break;
                if (candidate.Value >= startDate.ToDateOnly())
                    yield return LocalDateValue.FromDateOnly(candidate.Value);
            }

            // Guard against stepping past December 9999, where AddMonths would overflow.
            var nextMonthIndex = (long)current.Year * 12 + (current.Month - 1) + interval;
            if (nextMonthIndex > 9999L * 12 + 11) yield break;
            current = current.AddMonths(interval);
            if (current > end) yield break;
        }
    }

    private static DateOnly? ResolveMonthlyDate(int year, int month, MonthlyRule rule)
    {
        if (rule is DayOfMonthMonthlyRule domRule)
        {
            var daysInMonth = DateTime.DaysInMonth(year, month);
            if (domRule.Day > daysInMonth) return null;
            return new DateOnly(year, month, domRule.Day);
        }

        if (rule is NthWeekdayMonthlyRule nthRule)
        {
            return FindNthWeekday(year, month, nthRule.WeekIndex, nthRule.Weekday.ToDayOfWeek());
        }

        if (rule is LastDayOfMonthMonthlyRule)
        {
            return new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        }

        return null;
    }

    private static IEnumerable<LocalDateValue> GenerateYearly(
        LocalDateValue startDate, int interval, YearlyRule yearly, LocalDateValue endDate)
    {
        var currentYear = startDate.Year;
        var end = endDate.ToDateOnly();

        while (true)
        {
            var candidate = ResolveYearlyDate(currentYear, yearly);
            if (candidate != null)
            {
                if (candidate.Value > end) yield break;
                if (candidate.Value >= startDate.ToDateOnly())
                    yield return LocalDateValue.FromDateOnly(candidate.Value);
            }

            // Guard against stepping past year 9999, where DateOnly construction would overflow.
            var nextYear = (long)currentYear + interval;
            if (nextYear > 9999) yield break;
            currentYear = (int)nextYear;
            if (new DateOnly(currentYear, 1, 1) > end) yield break;
        }
    }

    private static DateOnly? ResolveYearlyDate(int year, YearlyRule rule)
    {
        if (rule is DayOfMonthYearlyRule domRule)
        {
            var daysInMonth = DateTime.DaysInMonth(year, domRule.Month);
            if (domRule.Day > daysInMonth) return null;
            return new DateOnly(year, domRule.Month, domRule.Day);
        }

        if (rule is NthWeekdayYearlyRule nthRule)
        {
            return FindNthWeekday(year, nthRule.Month, nthRule.WeekIndex, nthRule.Weekday.ToDayOfWeek());
        }

        return null;
    }

    private static DateOnly? FindNthWeekday(int year, int month, int weekIndex, DayOfWeek dayOfWeek)
    {
        if (weekIndex == -1)
        {
            // Last occurrence
            var lastDay = DateTime.DaysInMonth(year, month);
            var date = new DateOnly(year, month, lastDay);
            while (date.DayOfWeek != dayOfWeek)
                date = date.AddDays(-1);
            return date;
        }

        // Nth occurrence
        var first = new DateOnly(year, month, 1);
        var offset = ((int)dayOfWeek - (int)first.DayOfWeek + 7) % 7;
        var nthDate = first.AddDays(offset + (weekIndex - 1) * 7);

        if (nthDate.Month != month) return null;
        return nthDate;
    }
}
