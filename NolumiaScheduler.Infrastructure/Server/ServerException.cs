using System.Net;

namespace NolumiaScheduler.Infrastructure.Server;

/// <summary>
/// A call to the Scheduler server failed. <see cref="StatusCode"/> is null when the server could not
/// be reached at all; <see cref="Reason"/> is the server's <c>reason</c> field when it sent one.
/// </summary>
public class ServerException(string message, HttpStatusCode? statusCode = null, string? reason = null, Exception? innerException = null)
    : Exception(message, innerException)
{
    public HttpStatusCode? StatusCode { get; } = statusCode;
    public string? Reason { get; } = reason;

    /// <summary>The server could not be reached (offline, DNS, timeout) — the local copy stays in use.</summary>
    public bool IsUnreachable => StatusCode is null;

    /// <summary>The event changed on the server since it was read (409 on <c>expected_version</c>).</summary>
    public bool IsConflict => StatusCode == HttpStatusCode.Conflict;
}

/// <summary>Nobody is signed in, or the sign-in expired and could not be renewed.</summary>
public sealed class ServerNotSignedInException()
    : ServerException("Not signed in to the Scheduler server.", HttpStatusCode.Unauthorized);
