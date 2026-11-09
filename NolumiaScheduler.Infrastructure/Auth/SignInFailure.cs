using System.Net;

namespace NolumiaScheduler.Infrastructure.Auth;

/// <summary>
/// Why a sign-in failed. The screen words each kind in the app's language (Presentation's
/// <c>SignInFailure*</c> strings); <see cref="SignInException.Detail"/> carries the part that varies.
/// The exception messages stay in English for the log.
/// </summary>
public enum SignInFailure
{
    /// <summary>The loopback port is taken (detail: host:port).</summary>
    LoopbackUnavailable,
    /// <summary>assay sent the browser back with an error (detail: error and description).</summary>
    Refused,
    /// <summary>The answer was not for the sign-in this PC started (detail: state or nonce).</summary>
    Mismatch,
    /// <summary>The answer had no authorization code.</summary>
    NoCode,
    /// <summary>No refresh token came back (offline_access).</summary>
    NoRefreshToken,
    /// <summary>The ID token could not be read.</summary>
    IdTokenUnreadable,
    /// <summary>assay could not be reached, or its settings (discovery) were empty.</summary>
    ServiceUnreachable,
    /// <summary>The token endpoint refused (detail: status and OAuth error).</summary>
    ServiceRefused,
    /// <summary>The token endpoint's answer could not be read.</summary>
    ServiceReplyUnreadable,
}

/// <summary>
/// The page the browser shows once the sign-in has come back (the app's language, from the caller).
/// The default is English, like the app's default language.
/// </summary>
public sealed record SignInPage(string Language, string Title, string Heading, string Body)
{
    public static readonly SignInPage English = new(
        "en", "NolumiaScheduler", "Signed in to NolumiaScheduler", "You can close this tab and go back to NolumiaScheduler.");

    /// <summary>The whole HTML document, every text escaped.</summary>
    public string ToHtml() =>
        $"""
        <!doctype html><html lang="{WebUtility.HtmlEncode(Language)}"><head><meta charset="utf-8"><title>{WebUtility.HtmlEncode(Title)}</title></head>
        <body style="font-family: 'Segoe UI', 'Yu Gothic UI', sans-serif; margin: 3em;">
        <h1 style="font-size: 1.4em;">{WebUtility.HtmlEncode(Heading)}</h1>
        <p>{WebUtility.HtmlEncode(Body)}</p></body></html>
        """;
}
