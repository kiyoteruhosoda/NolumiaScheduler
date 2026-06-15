namespace NolumiaScheduler.Cli;

/// <summary>
/// Minimal argument model: the command name, ordered positional arguments and
/// <c>--name value</c> / <c>--flag</c> options. Kept dependency-free on purpose.
/// </summary>
internal sealed class CliArgs
{
    public string? Command { get; private init; }
    public IReadOnlyList<string> Positionals { get; private init; } = [];
    private readonly Dictionary<string, string?> _options = new(StringComparer.OrdinalIgnoreCase);

    public static CliArgs Parse(string[] args)
    {
        string? command = null;
        var positionals = new List<string>();
        var options = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                var name = arg[2..];
                // "--name value" unless the next token is another option or absent (then it is a flag).
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    options[name] = args[++i];
                else
                    options[name] = null;
            }
            else if (command == null)
            {
                command = arg;
            }
            else
            {
                positionals.Add(arg);
            }
        }

        var parsed = new CliArgs { Command = command, Positionals = positionals };
        foreach (var kv in options) parsed._options[kv.Key] = kv.Value;
        return parsed;
    }

    public string? Option(string name) => _options.TryGetValue(name, out var v) ? v : null;

    public bool HasFlag(string name) => _options.ContainsKey(name);

    public string? Positional(int index) => index < Positionals.Count ? Positionals[index] : null;
}
