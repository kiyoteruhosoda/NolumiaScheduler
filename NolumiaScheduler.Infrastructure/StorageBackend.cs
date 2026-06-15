namespace NolumiaScheduler.Infrastructure;

/// <summary>
/// Selects which persistence implementation the composition root wires up.
/// The default backend is <see cref="Json"/>; SQLite is opt-in.
/// </summary>
public enum StorageBackend
{
    /// <summary>One JSON file per aggregate under the application data folder (default).</summary>
    Json,

    /// <summary>A single SQLite database file under the application data folder.</summary>
    Sqlite,
}
