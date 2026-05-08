namespace NolumiaSchedulerTest.E2E;

public enum UiTestExecutionStatus
{
    Passed,
    Inconclusive,
    Failed,
}

public sealed record UiTestExecutionResult(
    UiTestExecutionStatus Status,
    string Message,
    int ExitCode = 0);

public interface IUiTestDriver
{
    string Name { get; }
    UiTestExecutionResult ExecuteSmokeTest();
}

public sealed class DotNetUnavailableUiTestDriver : IUiTestDriver
{
    public string Name => "dotnet-unavailable";

    public UiTestExecutionResult ExecuteSmokeTest()
        => new(
            UiTestExecutionStatus.Inconclusive,
            "dotnet コマンドが見つからないため UI テストは実行できませんでした。");
}

public sealed class PlaywrightUnavailableUiTestDriver : IUiTestDriver
{
    public string Name => "playwright-unavailable";

    public UiTestExecutionResult ExecuteSmokeTest()
        => new(
            UiTestExecutionStatus.Inconclusive,
            "Playwright CLI が見つからないため UI テストは実行できませんでした。必要なら 'npx playwright test' 実行環境を構築してください。");
}

public static class UiTestDriverFactory
{
    public static IUiTestDriver Create(IReadOnlyDictionary<string, string?> environment)
    {
        var hasDotNet = HasCommand(environment, "DOTNET_HOST_PATH") || HasPathLike(environment, "PATH", "dotnet");
        if (!hasDotNet)
        {
            return new DotNetUnavailableUiTestDriver();
        }

        var hasPlaywright = HasPathLike(environment, "PATH", "playwright")
            || HasPathLike(environment, "PATH", "node")
            || HasPathLike(environment, "PATH", "npm")
            || HasPathLike(environment, "PATH", "npx");

        return hasPlaywright
            ? new PlaywrightUnavailableUiTestDriver() // 実行基盤導入後に real driver を差し替える
            : new PlaywrightUnavailableUiTestDriver();
    }

    private static bool HasCommand(IReadOnlyDictionary<string, string?> env, string key)
        => env.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value);

    private static bool HasPathLike(IReadOnlyDictionary<string, string?> env, string key, string needle)
        => env.TryGetValue(key, out var value)
           && !string.IsNullOrWhiteSpace(value)
           && value.Contains(needle, StringComparison.OrdinalIgnoreCase);
}
