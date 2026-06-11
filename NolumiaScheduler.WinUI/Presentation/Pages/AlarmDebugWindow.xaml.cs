using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using NolumiaScheduler.Application.Services;
using NolumiaScheduler.Domain.Services;
using NolumiaScheduler.Domain.ValueObjects;
using NolumiaScheduler.Presentation.Services;

namespace NolumiaScheduler.WinUI.Presentation.Pages;

public sealed partial class AlarmDebugWindow : Window
{
    private readonly IAlarmService _alarmService;
    private readonly CalendarEventApplicationService _eventService;
    private readonly IOccurrenceExpander _expander;
    private readonly TimeProvider _clock;
    private DispatcherQueueTimer? _refreshTimer;
    private DispatcherQueueTimer? _clockTimer;

    public AlarmDebugWindow(
        IAlarmService alarmService,
        CalendarEventApplicationService eventService,
        IOccurrenceExpander expander,
        TimeProvider clock)
    {
        InitializeComponent();

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
        sb.AppendLine($"=== アラームデバッグ {_clock.GetLocalNow():yyyy/MM/dd HH:mm:ss} ===");
        AppendSection(sb, "サマリー", SummaryLabel.Text);
        AppendSection(sb, "全イベント (リポジトリ生データ)", AllEventsLabel.Text);
        AppendSection(sb, "オカレンス展開結果 (today～tomorrow)", OccurrenceLabel.Text);
        AppendSection(sb, "アラームエンジン判定パイプライン", EngineDiagLabel.Text);
        AppendSection(sb, "発火済キー一覧", FiredKeysLabel.Text);

        var alarmItems = AlarmList.ItemsSource as IEnumerable<AlarmDebugItem> ?? [];
        AppendSection(sb, "スケジュール済アラーム", string.Join("\n", alarmItems.Select(i => i.ToClipboardLine())));

        var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
        package.SetText(sb.ToString());
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
    }

    private static void AppendSection(System.Text.StringBuilder sb, string title, string body)
    {
        sb.AppendLine();
        sb.AppendLine($"■ {title}");
        sb.AppendLine(string.IsNullOrWhiteSpace(body) ? "(なし)" : body);
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
            $"現在時刻      : {now:yyyy/MM/dd HH:mm:ss}",
            $"検索範囲      : {today} ～ {tomorrow}",
            $"イベント総数  : {allEvents.Count}",
            $"  Alarm有効   : {allEvents.Count(e => e.Alarm is { IsEnabled: true })}",
            $"  Alarm無効/null: {allEvents.Count(e => e.Alarm == null || !e.Alarm.IsEnabled)}",
            $"発火済キー数  : {firedKeys.Count}",
            $"タイマー稼働  : {(_refreshTimer?.IsRunning == true ? "はい" : "いいえ")}",
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
            evLines.Add($"  AllDay   : {ev.AllDay}");

            if (ev.Alarm == null)
                evLines.Add($"  Alarm    : null ← ★アラーム未設定");
            else
                evLines.Add($"  Alarm    : Enabled={ev.Alarm.IsEnabled}, 15m={ev.Alarm.Notify15Min}, 5m={ev.Alarm.Notify5Min}, 1m={ev.Alarm.Notify1Min}"
                    + (!ev.Alarm.IsEnabled ? " ← ★無効" : ""));

            if (ev.IsSingle() && ev.SingleSchedule is { } ss)
            {
                evLines.Add($"  Schedule : Single");
                evLines.Add($"    Start  : {ss.Start:yyyy/MM/dd HH:mm:ss zzz}");
                evLines.Add($"    End    : {ss.End:yyyy/MM/dd HH:mm:ss zzz}");
                try
                {
                    var tz = ev.TimeZoneId.ToTimeZoneInfo();
                    var localStart = TimeZoneInfo.ConvertTime(ss.Start, tz);
                    var localDate = LocalDateValue.FromDateOnly(DateOnly.FromDateTime(localStart.DateTime));
                    evLines.Add($"    Local  : {localStart:yyyy/MM/dd HH:mm:ss} ({ev.TimeZoneId.Value})");
                    evLines.Add($"    LocalDate: {localDate}");
                    var inRange = localDate >= today && localDate <= tomorrow;
                    evLines.Add($"    範囲内? : {inRange}" + (!inRange ? $" ← ★範囲外 (today={today}, tomorrow={tomorrow})" : ""));
                }
                catch (Exception ex)
                {
                    evLines.Add($"    TZ変換エラー: {ex.Message}");
                }
            }
            else if (ev.RecurringSchedule is { } rs)
            {
                evLines.Add($"  Schedule : Recurring");
                evLines.Add($"    StartDate : {rs.StartDate}");
                evLines.Add($"    Time      : {rs.StartTime} ～ {rs.EndTime}");
                evLines.Add($"    AllDay    : {rs.AllDay}");
                evLines.Add($"    Rule      : {rs.RecurrenceRule.RuleType}, Interval={rs.RecurrenceRule.Interval}");
                evLines.Add($"    EndDate   : {rs.RecurrenceRule.EndDate}");
            }
            else
            {
                evLines.Add($"  Schedule : ??? (SingleSchedule=null, RecurringSchedule=null)");
            }

            if (ev.Exceptions.Count > 0)
                evLines.Add($"  Exceptions: {ev.Exceptions.Count}件");
            if (ev.Moves.Count > 0)
                evLines.Add($"  Moves     : {ev.Moves.Count}件");
            evLines.Add("");
        }
        AllEventsLabel.Text = evLines.Count > 0 ? string.Join("\n", evLines) : "(イベントなし)";

        // ── Section 3: Occurrence Expansion ──
        var occLines = new List<string>();
        foreach (var ev in allEvents)
        {
            var occurrences = _expander.Expand(ev, today, tomorrow, null);
            occLines.Add($"▶ {ev.Title.Value} → {occurrences.Count}件");
            if (occurrences.Count == 0)
            {
                occLines.Add($"  (範囲内のオカレンスなし)");
            }
            foreach (var occ in occurrences)
            {
                var startStr = occ.StartTime != null ? $"{occ.StartTime.Hour:D2}:{occ.StartTime.Minute:D2}" : "null";
                var endStr = occ.EndTime != null ? $"{occ.EndTime.Hour:D2}:{occ.EndTime.Minute:D2}" : "null";
                var flags = new List<string>();
                if (occ.AllDay) flags.Add("AllDay");
                if (occ.IsMoved) flags.Add("Moved");
                if (occ.IsOverridden) flags.Add("Overridden");
                if (occ.StartTime == null) flags.Add("★StartTime=null");
                var flagStr = flags.Count > 0 ? $" [{string.Join(", ", flags)}]" : "";
                occLines.Add($"  {occ.Date} {startStr}～{endStr}{flagStr}");
            }
            occLines.Add("");
        }
        OccurrenceLabel.Text = occLines.Count > 0 ? string.Join("\n", occLines) : "(イベントなし)";

        // ── Section 4: Engine Pipeline ──
        var engineDiag = _alarmService.GetDiagnosticLines();
        EngineDiagLabel.Text = string.Join("\n", engineDiag);

        // ── Section 5: Fired Keys ──
        FiredKeysLabel.Text = firedKeys.Count > 0
            ? string.Join("\n", firedKeys.Select(k => $"  {k}"))
            : "(なし)";

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
                ? "  [スヌーズ 発火待ち]"
                : $"  [スヌーズ あと{remaining.TotalMinutes:F0}分]";
            StatusBrush = new SolidColorBrush(Colors.Orange);
        }
        else if (entry.AlreadyFired)
        {
            StatusBadge = "  [発火済]";
            StatusBrush = new SolidColorBrush(Colors.Gray);
        }
        else if (now > entry.NotifyAt + AlarmScheduleCalculator.CatchUpGrace)
        {
            // The notify time passed beyond the catch-up grace: this alarm will never ring
            // (e.g. the event was already over when the app started).
            StatusBadge = "  [期限切れ]";
            StatusBrush = new SolidColorBrush(Colors.Gray);
        }
        else if (remaining.TotalSeconds <= 0)
        {
            StatusBadge = "  [発火待ち]";
            StatusBrush = new SolidColorBrush(Colors.Red);
        }
        else
        {
            StatusBadge = $"  [あと{remaining.TotalMinutes:F0}分]";
            StatusBrush = new SolidColorBrush(Colors.Green);
        }

        Detail = entry.IsSnoozed
            ? "スヌーズ再通知"
            : $"開始: {entry.OccurrenceStart:HH:mm}  |  {entry.MinutesBefore}分前通知";

        NotifyAtText = $"通知時刻: {entry.NotifyAt:yyyy/MM/dd HH:mm:ss}  |  EventId: {entry.EventId}";
    }

    public string ToClipboardLine()
        => $"{Title}{StatusBadge} | {Detail} | {NotifyAtText}";
}
