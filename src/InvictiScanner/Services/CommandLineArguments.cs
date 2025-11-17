namespace InvictiScanner.Services;

internal sealed class CommandLineArguments
{
    private readonly Dictionary<string, string> _arguments;

    private CommandLineArguments(Dictionary<string, string> arguments)
    {
        _arguments = arguments;
    }

    public string? this[string key]
    {
        get
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return null;
            }

            return _arguments.TryGetValue(Normalize(key), out var value) ? value : null;
        }
    }

    public static CommandLineArguments Parse(string[] args)
    {
        var dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < args.Length; i++)
        {
            var current = args[i];
            if (!current.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var key = Normalize(current[2..]);
            var hasExplicitValue = key.Contains('=');
            if (hasExplicitValue)
            {
                var parts = key.Split('=', 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2)
                {
                    dictionary[parts[0]] = parts[1];
                }
                continue;
            }

            if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                dictionary[key] = args[i + 1];
                i += 1;
            }
            else
            {
                dictionary[key] = "true";
            }
        }

        return new CommandLineArguments(dictionary);
    }

    private static string Normalize(string value) => value.Trim().ToLowerInvariant();
}
