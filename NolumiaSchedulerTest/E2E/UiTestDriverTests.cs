namespace NolumiaSchedulerTest.E2E;

[TestClass]
public class UiTestDriverTests
{
    [TestMethod]
    public void UI_SmokeTestDriver_AlwaysReturnsDeterministicResult()
    {
        var env = Environment.GetEnvironmentVariables()
            .Cast<System.Collections.DictionaryEntry>()
            .GroupBy(entry => entry.Key.ToString()!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Last().Value?.ToString(),
                StringComparer.OrdinalIgnoreCase);

        var driver = UiTestDriverFactory.Create(env);
        var result = driver.ExecuteSmokeTest();

        Assert.IsTrue(result.Status is UiTestExecutionStatus.Passed or UiTestExecutionStatus.Inconclusive);
        Assert.IsFalse(string.IsNullOrWhiteSpace(driver.Name));
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.Message));
    }
}
