using System.Globalization;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using NolumiaScheduler.Infrastructure.Diagnostics;
using NolumiaScheduler.Infrastructure.ExternalCalendars;
using NolumiaScheduler.Infrastructure.Server;
using NolumiaScheduler.Presentation.Resources.Strings;
using NolumiaScheduler.WinUI.Diagnostics;
using NolumiaScheduler.WinUI.Presentation.Services;

namespace NolumiaScheduler.WinUI.Presentation.Pages;

/// <summary>
/// Shows whether Outlook events reach the app: the server sign-in and copy, the Power Automate file,
/// the latest sends and reads, and the findings <see cref="SyncStatus"/> draws from them. Every time
/// is the PC's local time with its UTC offset. The decisions live in <see cref="SyncStatus"/>
/// (Infrastructure, covered by CoreTests); this window only writes them out and offers the buttons.
/// </summary>
public sealed partial class SyncStatusWindow : Window
{
    private readonly IServiceProvider _services;
    private readonly TimeProvider _clock;
    private readonly DispatcherQueueTimer _refreshTimer;
    private bool _closed;

    public SyncStatusWindow(IServiceProvider services)
    {
        InitializeComponent();

        Title = AppResources.SyncStatusTitle;
        SendNowButton.Content = AppResources.SyncStatusSendNow;
        ReadNowButton.Content = AppResources.SyncStatusReadNow;
        CopyButton.Content = AppResources.SyncStatusCopy;
        OpenLogFolderButton.Content = AppResources.SyncStatusOpenLogFolder;
        ProblemsHeaderLabel.Text = $"■ {AppResources.SyncStatusProblemsHeader}";
        ServerHeaderLabel.Text = $"■ {AppResources.SyncStatusServerHeader}";
        OutlookHeaderLabel.Text = $"■ {AppResources.SyncStatusOutlookHeader}";
        TodayHeaderLabel.Text = $"■ {AppResources.SyncStatusTodayHeader}";
        LogHeaderLabel.Text = $"■ {AppResources.SyncStatusLogHeader}";

        _services = services;
        _clock = services.GetRequiredService<TimeProvider>();

        AppWindow?.Resize(new Windows.Graphics.SizeInt32(760, 900));

        // Sends and reads finish in the background; a short tick shows their outcome without a click.
        _refreshTimer = DispatcherQueue.CreateTimer();
        _refreshTimer.Interval = TimeSpan.FromSeconds(3);
        _refreshTimer.Tick += (_, _) => Refresh();
        _refreshTimer.Start();

        Activated += (_, _) => Refresh();
        Closed += (_, _) =>
        {
            _closed = true;
            _refreshTimer.Stop();
        };
        Refresh();
    }

    private void OnSendNowClicked(object sender, RoutedEventArgs e)
        => _services.GetRequiredService<ExternalCalendarSyncHost>().SyncNow();

    private void OnReadNowClicked(object sender, RoutedEventArgs e)
        => _services.GetService<ServerRefreshHost>()?.RefreshNow("manual");

    private void OnCopyClicked(object sender, RoutedEventArgs e)
    {
        Refresh();
        var sb = new StringBuilder();
        sb.AppendLine($"=== {AppResources.SyncStatusTitle} — {ClockLabel.Text} ===");
        AppendSection(sb, AppResources.SyncStatusProblemsHeader, ProblemsLabel.Text);
        AppendSection(sb, AppResources.SyncStatusServerHeader, ServerLabel.Text);
        AppendSection(sb, AppResources.SyncStatusOutlookHeader, OutlookLabel.Text);
        AppendSection(sb, AppResources.SyncStatusTodayHeader, TodayLabel.Text);
        AppendSection(sb, AppResources.SyncStatusLogHeader, LogLabel.Text);

        var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
        package.SetText(sb.ToString());
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
    }

    private void OnOpenLogFolderClicked(object sender, RoutedEventArgs e)
    {
        var dir = AppDiagnostics.LogDirectory;
        Directory.CreateDirectory(dir);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = dir, UseShellExecute = true });
    }

    private static void AppendSection(StringBuilder sb, string title, string body)
    {
        sb.AppendLine();
        sb.AppendLine($"■ {title}");
        sb.AppendLine(string.IsNullOrWhiteSpace(body) ? "(none)" : body);
    }

    private void Refresh()
    {
        if (_closed) return;
        var status = SyncStatus.Collect(Sources());

        ClockLabel.Text = DiagnosticTime.Format(status.Now, status.Zone);
        ProblemsLabel.Text = status.Problems.Count == 0
            ? AppResources.SyncStatusNoProblems
            : string.Join("\n", status.Problems.Select(p => "• " + Describe(p)));
        ServerLabel.Text = ServerText(status);
        OutlookLabel.Text = OutlookText(status);
        TodayLabel.Text = TodayText(status);
        LogLabel.Text = status.RecentLog.Count == 0 ? "(none)" : string.Join("\n", status.RecentLog);
    }

    private SyncStatusSources Sources()
    {
        var server = _services.GetRequiredService<ServerConnection>();
        return new SyncStatusSources(
            _clock,
            server.Settings.BaseUri.Host,
            server.SignIn.IsSignedIn,
            server.SignIn.UserName,
            ServerEventMigrator.ReadMarker(server.DataDirectory)?.MigratedAt,
            _services.GetService<ServerCalendarStore>(),
            _services.GetRequiredService<ExternalCalendarConfig>(),
            _services.GetRequiredService<OutlookFeedSender>().CalendarId,
            _services.GetRequiredService<SyncActivity>(),
            FileAppLog.PathFor(AppDiagnostics.LogDirectory, _clock.GetLocalNow()));
    }

    private static string Describe(SyncProblem problem) => problem switch
    {
        SyncProblem.NotSignedIn => AppResources.SyncProblemNotSignedIn,
        SyncProblem.NotMovedToServer => AppResources.SyncProblemNotMovedToServer,
        SyncProblem.OutlookDisabled => AppResources.SyncProblemOutlookDisabled,
        SyncProblem.OutlookFileMissing => AppResources.SyncProblemOutlookFileMissing,
        SyncProblem.OutlookFileOld => AppResources.SyncProblemOutlookFileOld,
        SyncProblem.OutlookFileNotSentYet => AppResources.SyncProblemOutlookFileNotSentYet,
        SyncProblem.OutlookSendFailed => AppResources.SyncProblemOutlookSendFailed,
        SyncProblem.ServerReadFailed => AppResources.SyncProblemServerReadFailed,
        SyncProblem.ServerCopyOld => AppResources.SyncProblemServerCopyOld,
        SyncProblem.OutlookNotInCopy => AppResources.SyncProblemOutlookNotInCopy,
        SyncProblem.ItemsSkipped => AppResources.SyncProblemItemsSkipped,
        _ => problem.ToString(),
    };

    // ── Body text (fixed English labels, like the log it is compared with) ──

    private static string ServerText(SyncStatus s)
    {
        var lines = new List<string>
        {
            $"Server          : {s.ServerHost}",
            $"Signed in       : {(s.SignedIn ? $"yes{(s.UserName is { Length: > 0 } n ? $" ({n})" : "")}" : "no")}",
            $"Moved to server : {(s.MovedAt is { } moved ? Time(s, moved) : "no (events are on this PC)")}",
            $"Copy read at    : {(s.FetchedAt is { } fetched ? Time(s, fetched) : "never")}",
            $"Last read       : {Attempt(s, s.LastServerRead)}",
            $"Copy holds      : {s.EventCount} events, {s.ImportedCount} imported occurrences",
            $"  from Outlook  : {s.OutlookCount}{(s.OutlookFirst is { } first && s.OutlookLast is { } last ? $" ({Date(s, first)} .. {Date(s, last)})" : "")}",
            $"Not readable    : {s.Skipped.Count}",
        };
        lines.AddRange(s.Skipped.Select(x => "  - " + x));
        return string.Join("\n", lines);
    }

    private static string OutlookText(SyncStatus s)
    {
        var lines = new List<string>
        {
            $"Sending         : {(s.OutlookEnabled ? $"on, every {s.OutlookIntervalMinutes} min and 5 s after the file changes" : "off (external-calendar.json)")}",
            $"File            : {s.OutlookFilePath}",
            $"File written    : {(s.OutlookFileExists ? $"{(s.OutlookFileWrittenAt is { } w ? Time(s, w) : "?")}, {s.OutlookFileSize?.ToString("N0", CultureInfo.InvariantCulture)} bytes" : "missing")}",
            $"Server calendar : {(s.OutlookCalendarId is { } id ? $"#{id}" : "not created yet")}",
            $"Last attempt    : {Attempt(s, s.LastOutlookSend)}",
            $"Last sent       : {Attempt(s, s.LastOutlookSent)}",
        };
        return string.Join("\n", lines);
    }

    private static string TodayText(SyncStatus s)
    {
        if (s.OutlookToday.Count == 0) return "(none)";
        return string.Join("\n", s.OutlookToday.Select(o =>
        {
            if (o.IsAllDay) return $"all day      {o.Title}";
            var start = TimeZoneInfo.ConvertTime(o.Start, s.Zone);
            var end = start.AddMinutes(o.DurationMinutes);
            return $"{start:HH:mm}-{end:HH:mm}  {o.Title}";
        }));
    }

    private static string Attempt(SyncStatus s, SyncAttempt? a)
        => a is null
            ? "none since the app started"
            : $"{Time(s, a.At)} [{a.Reason}] {a.Result}{(a.Error is { } e ? $" — {e}" : "")}";

    private static string Time(SyncStatus s, DateTimeOffset moment)
        => $"{DiagnosticTime.Format(moment, s.Zone)}, {Ago(s.Now - moment)}";

    private static string Date(SyncStatus s, DateTimeOffset moment)
        => TimeZoneInfo.ConvertTime(moment, s.Zone).ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);

    private static string Ago(TimeSpan age)
    {
        if (age < TimeSpan.FromMinutes(1)) return "just now";
        if (age < TimeSpan.FromHours(2)) return $"{(int)age.TotalMinutes} min ago";
        if (age < TimeSpan.FromDays(2)) return $"{(int)age.TotalHours} h ago";
        return $"{(int)age.TotalDays} d ago";
    }
}
