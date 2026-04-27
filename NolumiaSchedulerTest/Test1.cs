namespace NolumiaSchedulerTest
{
    [TestClass]
    public partial class Test1 : PageTest
    {
        [TestMethod]
        public async Task HomepageHasPlaywrightInTitleAndGetStartedLinkLinkingToTheIntroPage()
        {
            await Page.GotoAsync("https://playwright.dev");

            // Expect a title "to contain" a substring.
            await Expect(Page).ToHaveTitleAsync(PlaywrightRegex());

            // create a locator
            var getStarted = Page.Locator("text=Get Started");

            // Expect an attribute "to be strictly equal" to the value.
            await Expect(getStarted).ToHaveAttributeAsync("href", "/docs/intro");

            // Click the get started link.
            await getStarted.ClickAsync();

            // Expects the URL to contain intro.
            await Expect(Page).ToHaveURLAsync(IntroRegex());
        }

        [GeneratedRegex("Playwright")]
        private static partial Regex PlaywrightRegex();

        [GeneratedRegex(".*intro")]
        private static partial Regex IntroRegex();
    }
}
