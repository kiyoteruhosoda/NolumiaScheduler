namespace NolumiaScheduler.Infrastructure.Diagnostics;

/// <summary>
/// Reads the last records of a <see cref="FileAppLog"/> file that matter for syncing: everything of
/// the given categories and every warning or worse. A record's indented exception block is reduced
/// to its first line (the exception type and message).
/// </summary>
public static class AppLogTail
{
    private static readonly string[] SeriousLevels = ["WARNING", "ERROR", "FATAL"];

    public static IReadOnlyList<string> Read(string path, IReadOnlyCollection<string> categories, int maxRecords)
    {
        string[] lines;
        try
        {
            if (!File.Exists(path)) return [];
            // The app keeps appending while we read; FileAppLog opens with FileShare.ReadWrite too.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            lines = reader.ReadToEnd().Split('\n');
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        var records = new List<string>();
        var keepDetail = false;
        foreach (var raw in lines)
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0) continue;

            if (line.StartsWith("    ", StringComparison.Ordinal))
            {
                // First line of the exception block: "System.X: message".
                if (keepDetail) records[^1] += Environment.NewLine + line;
                keepDetail = false;
                continue;
            }

            keepDetail = Matches(line, categories);
            if (keepDetail) records.Add(line);
        }

        return records.Count <= maxRecords ? records : records.GetRange(records.Count - maxRecords, maxRecords);
    }

    private static bool Matches(string line, IReadOnlyCollection<string> categories)
    {
        // "2026-10-21 09:15:06.555 +09:00 WARNINGpid=…": the level is the fourth space-separated token
        // (padded to 7 characters, so a WARNING runs straight into "pid=").
        var tokens = line.Split(' ', 5, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length >= 4)
            foreach (var level in SeriousLevels)
                if (tokens[3].StartsWith(level, StringComparison.Ordinal)) return true;
        foreach (var category in categories)
            if (line.Contains("[" + category + "]", StringComparison.Ordinal)) return true;
        return false;
    }
}
