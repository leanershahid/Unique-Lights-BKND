namespace UniqueLights.Api.Extensions;

/// <summary>
/// Minimal .env loader for local development. Reads KEY=VALUE lines (supports comments, blank lines,
/// optional "export " prefix and single/double quoted values). Existing environment variables are never overwritten.
/// </summary>
public static class DotEnvLoader
{
    public static void Load(string directory, string fileName = ".env")
    {
        var path = Path.Combine(directory, fileName);
        if (!File.Exists(path)) return;

        foreach (var rawLine in File.ReadAllLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            if (line.StartsWith("export ", StringComparison.Ordinal)) line = line[7..].TrimStart();

            var separator = line.IndexOf('=');
            if (separator <= 0) continue;

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();

            if (value.Length >= 2 && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
            {
                value = value[1..^1];
            }

            if (Environment.GetEnvironmentVariable(key) is null)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }
}
