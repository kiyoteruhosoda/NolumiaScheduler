namespace NolumiaScheduler.Infrastructure.Auth;

/// <summary>Where the Bearer token for the Scheduler server comes from (the assay sign-in).</summary>
public interface IAccessTokenSource
{
    /// <summary>A usable access token, or null when nobody is signed in.</summary>
    Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken);

    /// <summary>The server refused this token (401); fetch a new one next time.</summary>
    void Invalidate(string accessToken);
}
