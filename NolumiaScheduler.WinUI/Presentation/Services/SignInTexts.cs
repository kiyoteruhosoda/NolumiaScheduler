using NolumiaScheduler.Infrastructure.Auth;
using NolumiaScheduler.Presentation.Resources.Strings;

namespace NolumiaScheduler.WinUI.Presentation.Services;

/// <summary>
/// The sign-in wording in the app's language: the page the browser shows once the sign-in comes back,
/// and what a failed sign-in says on the screen. Infrastructure only names the kind of failure
/// (<see cref="SignInFailure"/>) and keeps English messages for the log.
/// </summary>
internal static class SignInTexts
{
    public static SignInPage Page() => new(
        AppResources.FormatCulture.TwoLetterISOLanguageName == "ja" ? "ja" : "en",
        "NolumiaScheduler",
        AppResources.SignInPageHeading,
        AppResources.SignInPageBody);

    /// <summary>What to show for an error from signing in or from checking with a sign-in.</summary>
    public static string Describe(Exception exception) => exception switch
    {
        SignInException s => WithDetail(Text(s.Failure), s.Detail),
        TokenEndpointException t => WithDetail(Text(t.Failure), t.Detail),
        _ => exception.Message,
    };

    private static string WithDetail(string text, string? detail) =>
        detail is { Length: > 0 }
            ? string.Format(AppResources.FormatCulture, AppResources.SignInFailureWithDetail, text, detail)
            : text;

    private static string Text(SignInFailure failure) => failure switch
    {
        SignInFailure.LoopbackUnavailable => AppResources.SignInFailureLoopbackUnavailable,
        SignInFailure.Refused => AppResources.SignInFailureRefused,
        SignInFailure.Mismatch => AppResources.SignInFailureMismatch,
        SignInFailure.NoCode => AppResources.SignInFailureNoCode,
        SignInFailure.NoRefreshToken => AppResources.SignInFailureNoRefreshToken,
        SignInFailure.IdTokenUnreadable => AppResources.SignInFailureIdTokenUnreadable,
        SignInFailure.ServiceUnreachable => AppResources.SignInFailureServiceUnreachable,
        SignInFailure.ServiceRefused => AppResources.SignInFailureServiceRefused,
        SignInFailure.ServiceReplyUnreadable => AppResources.SignInFailureServiceReplyUnreadable,
        _ => failure.ToString(),
    };
}
