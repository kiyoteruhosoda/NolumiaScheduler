using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using NolumiaScheduler.Application.Services;
using NolumiaScheduler.Domain.Services;
using NolumiaScheduler.Domain.ValueObjects;
using NolumiaScheduler.Presentation.Resources.Strings;
using NolumiaScheduler.Presentation.Services;

namespace NolumiaScheduler.WinUI.Presentation.Pages;

public sealed partial class AlarmDebugWindow : Window
{
    private readonly IAlarmService _alarmService;
    private readonly CalendarEventApplicationService _eventService;
    private readonly IOccurrenceExpander _expander;
    private readonly TimeProvider _clock;
    private readonly DispatcherQueueTimer? _refreshTimer;
    private readonly DispatcherQueueTimer? _clockTimer;

    public AlarmDebugWindow(
        IAlarmService alarmService,
        CalendarEventApplicationService eventService,
        IOccurrenceExpander expander,
        TimeProvider clock)
    {
        InitializeComponent();

        // Set localized UI strings
        Title = AppResources.AlarmDebugTitle;
        RefreshButton.Content      = AppResources.AlarmDebugRefresh;
        TestAlarmButton.Content    = AppResources.AlarmDebugTestAlarm;
        CopyButton.Content         = AppResources.AlarmDebugCopy;
        OpenJsonFolderButton.Content = AppResources.AlarmDebugOpenFolder;
        SummaryHeaderLabel.Text        = $"■ {AppResources.AlarmDebugSummaryHeader}";
        AllEventsHeaderLabel.Text      = $"■ {AppResources.AlarmDebugAllEventsHeader}";
        OccurrenceHeaderLabel.Text     = $"■ {AppResources.AlarmDebugOccurrencesHeader}";
        EnginePipelineHeaderLabel.Text = $"■ {AppResources.AlarmDebugEnginePipelineHeader}";
        FiredKeysHeaderLabel.Text      = $"■ {AppResources.AlarmDebugFiredKeysHeader}";
        ScheduledAlarmsHeaderLabel.Text = $"■ {AppResources.AlarmDebugScheduledAlarmsHeader}";

        _alarmService = alarmService;
        _eventService = eventService;
        _expander = expander;
        _clock = clock;

        // Set window size
        if (AppWindow is not null)
        {
            AppWindow.Resize(new Windows.Graphics.SizeInt32(700, 900));
            AppWindow.Move(new Windows.Graphics.PointInt32(50, 50));
        }

        // 1-second clock
        _clockTimer = DispatcherQueue.CreateTimer();
        _clockTimer.Interval = TimeSpan.FromSeconds(1);
        _clockTimer.Tick += (_, _) => ClockLabel.Text = _clock.GetLocalNow().ToString("yyyy/MM/dd (ddd) HH:mm:ss");
        _clockTimer.Start();
        ClockLabel.Text = _clock.GetLocalNow().ToString("yyyy/MM/dd (ddd) HH:mm:ss");

        // Real-time refresh on schedule changes
        _alarmService.ScheduleChanged += OnScheduleChanged;

        // Fallback auto-refresh every 10 seconds
        _refreshTimer = DispatcherQueue.CreateTimer();
        _refreshTimer.Interval = TimeSpan.FromSeconds(10);
        _refreshTimer.Tick += (_, _) => Refresh();
        _refreshTimer.Start();

        Activated += (_, _) => Refresh();
        Closed += (_, _) =>
        {
            _closed = true;
            _clockTimer?.Stop();
            _refreshTimer?.Stop();
            _alarmService.ScheduleChanged -= OnScheduleChanged;
        };
    }

    private bool _closed;

    private void OnScheduleChanged()
    {
        if (_closed) return;
        DispatcherQueue?.TryEnqueue(Refresh);
    }
    private void OnRefreshClicked(object sender, RoutedEventArgs e) => Refresh();
    private async void OnTestAlarmClicked(object sender, RoutedEventArgs e) => await _alarmService.ShowTestAlarmAsync();

    private void OnCopyClicked(object sender, RoutedEventArgs e)
    {
        Refresh();

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"=== Alarm Debug {_clock.GetLocalNow():yyyy/MM/dd HH:mm:ss} ===");
        AppendSection(sb, AppResources.AlarmDebugSummaryHeader, SummaryLabel.Text);
        AppendSection(sb, AppResources.AlarmDebugAllEventsHeader, AllEventsLabel.Text);
        AppendSection(sb, AppResources.AlarmDebugOccurrencesHeader, OccurrenceLabel.Text);
        AppendSection(sb, AppResources.AlarmDebugEnginePipelineHeader, EngineDiagLabel.Text);
        AppendSection(sb, AppResources.AlarmDebugFiredKeysHeader, FiredKeysLabel.Text);

        var alarmItems = AlarmList.ItemsSource as IEnumerable<AlarmDebugItem> ?? [];
        AppendSection(sb, AppResources.AlarmDebugScheduledAlarmsHeader, string.Join("\n", alarmItems.Select(i => i.ToClipboardLine())));

        var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
        package.SetText(sb.ToString());
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
    }

    private void OnOpenJsonFolderClicked(object sender, RoutedEventArgs e)
    {
        // Events are saved under LocalApplicationData\NolumiaScheduler\events
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NolumiaScheduler", "events");
        try
        {
            if (!Directory.Exists(dir))
            {
                // If no events folder yet, create it so Explorer can open it.
                Directory.CreateDirectory(dir);
            }

            // Open the containing folder in File Explorer.
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true,
                Verb = "open"
            };
            System.Diagnostics.Process.Start(psi);
        }
        catch (Exception ex)
        {
            var dlg = new Microsoft.UI.Xaml.Controls.ContentDialog
            {
                Title = AppResources.AlarmDebugOpenFolderError,
                Content = ex.Message,
                CloseButtonText = AppResources.AlarmDebugCloseButton
            };
            _ = dlg.ShowAsync();
        }
    }

    private static void AppendSection(System.Text.StringBuilder sb, string title, string body)
    {
        sb.AppendLine();
        sb.AppendLine($"■ {title}");
        sb.AppendLine(string.IsNullOrWhiteSpace(body) ? "(none)" : body);
    }

    private void Refresh()
    {
        if (_closed) return;
        var now = _clock.GetLocalNow().DateTime;
        var today = new LocalDateValue(now.Year, now.Month, now.Day);
        var tomorrow = today.AddDays(1);
        var allEvents = _eventService.FindAll();
        var firedKeys = _alarmService.GetFiredKeys();

        // ── Section 1: Summary ──
        var summary = new List<string>
        {
            $"Current time  : {now:yyyy/MM/dd HH:mm:ss}",
            $"Search range  : {today} ～ {tomorrow}",
            $"Total events  : {allEvents.Count}",
            $"  Alarm on    : {allEvents.Count(e => e.Alarm is { IsEnabled: true })}",
            $"  Alarm off/null: {allEvents.Count(e => e.Alarm == null || !e.Alarm.IsEnabled)}",
            $"Fired keys    : {firedKeys.Count}",
            $"Timer running : {(_refreshTimer?.IsRunning == true ? "yes" : "no")}",
        };
        SummaryLabel.Text = string.Join("\n", summary);

        // ── Section 2: All Events Raw Data ──
        var evLines = new List<string>();
        foreach (var ev in allEvents)
        {
            evLines.Add($"━━━ {ev.Title.Value} ━━━");
            evLines.Add($"  ID       : {ev.Id.Value}");
            evLines.Add($"  Kind     : {ev.Kind}");
            evLines.Add($"  TZ       : {ev.TimeZoneId.Value}");

            if (ev.Alarm == null)
                evLines.Add($"  Alarm    : null ← ★alarm not set");
            else
                evLines.Add($"  Alarm    : Enabled={ev.Alarm.IsEnabled}, 15m={ev.Alarm.Notify15Min}, 5m={ev.Alarm.Notify5Min}, 1m={ev.Alarm.Notify1Min}"
                    + (!ev.Alarm.IsEnabled ? " ← ★disabled" : ""));

            if (ev.IsSingle() && ev.SingleSchedule is { } ss)
            {
                evLines.Add($"  Schedule : Single");
                evLines.Add($"    StartUtc : {ss.StartUtc:yyyy/MM/dd HH:mm:ss}Z");
                evLines.Add($"    Duration: {ss.DurationMinutes} min");
                try
                {
                    var tz = ev.TimeZoneId.ToTimeZoneInfo();
                    var local = TimeZoneInfo.ConvertTime(ss.StartUtc, tz);
                    var localDate = new LocalDateValue(local.Year, local.Month, local.Day);
                    evLines.Add($"    Local   : {local:yyyy/MM/dd HH:mm} ({ev.TimeZoneId.Value})");
                    var inRange = localDate >= today && localDate <= tomorrow;
                    evLines.Add($"    In range? : {inRange}" + (!inRange ? $" ← ★out of range (today={today}, tomorrow={tomorrow})" : ""));
                }
                catch (Exception ex)
                {
                    evLines.Add($"    TZ convert error: {ex.Message}");
                }
            }
            else if (ev.RecurringSchedule is { } rs)
            {
                evLines.Add($"  Schedule : Recurring");
                evLines.Add($"    AnchorUtc : {rs.AnchorUtc:yyyy/MM/dd HH:mm:ss}Z (+{rs.DurationMinutes} min)");
                evLines.Add($"    Rule      : {rs.RecurrenceRule.RuleType}, Interval={rs.RecurrenceRule.Interval}");
                evLines.Add($"    EndDate   : {rs.RecurrenceRule.EndDate}");
            }
            else
            {
                evLines.Add($"  Schedule : ??? (SingleSchedule=null, RecurringSchedule=null)");
            }

            if (ev.Exceptions.Count > 0)
                evLines.Add($"  Exceptions: {ev.Exceptions.Count}");
            if (ev.Moves.Count > 0)
                evLines.Add($"  Moves     : {ev.Moves.Count}");
            evLines.Add("");
        }
        AllEventsLabel.Text = evLines.Count > 0 ? string.Join("\n", evLines) : "(no events)";

        // ── Section 3: Occurrence Expansion ──
        var occLines = new List<string>();
        foreach (var ev in allEvents)
        {
            var occurrences = _expander.Expand(ev, today, tomorrow, null);
            occLines.Add($"▶ {ev.Title.Value} → {occurrences.Count}");
            if (occurrences.Count == 0)
            {
                occLines.Add($"  (no occurrences in range)");
            }
            foreach (var occ in occurrences)
            {
                var startStr = $"{occ.StartTime.Hour:D2}:{occ.StartTime.Minute:D2}";
                var flags = new List<string>();
                if (occ.StartMinuteOfDay == 0 && occ.DurationMinutes == 24 * 60) flags.Add("AllDay");
                if (occ.IsMoved) flags.Add("Moved");
                if (occ.IsOverridden) flags.Add("Overridden");
                if (occ.CrossesMidnight) flags.Add("CrossesMidnight");
                var flagStr = flags.Count > 0 ? $" [{string.Join(", ", flags)}]" : "";
                occLines.Add($"  {occ.Date} {startStr} (+{occ.DurationMinutes}min){flagStr}");
            }
            occLines.Add("");
        }
        OccurrenceLabel.Text = occLines.Count > 0 ? string.Join("\n", occLines) : "(no events)";

        // ── Section 4: Engine Pipeline ──
        var engineDiag = _alarmService.GetDiagnosticLines();
        EngineDiagLabel.Text = string.Join("\n", engineDiag);

        // ── Section 5: Fired Keys ──
        FiredKeysLabel.Text = firedKeys.Count > 0
            ? string.Join("\n", firedKeys.Select(k => $"  {k}"))
            : "(none)";

        // ── Section 6: Alarm Cards ──
        var entries = _alarmService.GetScheduledAlarms();
        AlarmList.ItemsSource = entries.Select(e => new AlarmDebugItem(e, now)).ToList();
    }
}

internal class AlarmDebugItem
{
    public string Title { get; }
    public string StatusBadge { get; }
    public SolidColorBrush StatusBrush { get; }
    public string Detail { get; }
    public string NotifyAtText { get; }

    public AlarmDebugItem(AlarmScheduleEntry entry, DateTime now)
    {
        Title = entry.Title;

        var remaining = entry.NotifyAt - now;

        if (entry.IsSnoozed)
        {
            // Due snoozes always fire on the next tick (no catch-up grace), so they are either
            // counting down or about to ring.
            StatusBadge = remaining.TotalSeconds <= 0
                ? "  [snooze pending]"
                : $"  [snooze in {remaining.TotalMinutes:F0} min]";
            StatusBrush = new SolidColorBrush(Colors.Orange);
        }
        else if (entry.AlreadyFired)
        {
            StatusBadge = "  [fired]";
            StatusBrush = new SolidColorBrush(Colors.Gray);
        }
        else if (now > entry.NotifyAt + AlarmScheduleCalculator.CatchUpGrace)
        {
            // The notify time passed beyond the catch-up grace: this alarm will never ring
            // (e.g. the event was already over when the app started).
            StatusBadge = "  [expired]";
            StatusBrush = new SolidColorBrush(Colors.Gray);
        }
        else if (remaining.TotalSeconds <= 0)
        {
            StatusBadge = "  [pending]";
            StatusBrush = new SolidColorBrush(Colors.Red);
        }
        else
        {
            StatusBadge = $"  [in {remaining.TotalMinutes:F0} min]";
            StatusBrush = new SolidColorBrush(Colors.Green);
        }

        Detail = entry.IsSnoozed
            ? "snooze reminder"
            : $"start: {entry.OccurrenceStart:HH:mm}  |  {entry.MinutesBefore} min before";

        NotifyAtText = $"notify at: {entry.NotifyAt:yyyy/MM/dd HH:mm:ss}  |  EventId: {entry.EventId}";
    }

    public string ToClipboardLine()
        => $"{Title}{StatusBadge} | {Detail} | {NotifyAtText}";
}
