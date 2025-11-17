using System.Text;

namespace InvictiScanner.Services;

internal static class HttpQueryBuilder
{
    public static string WithQuery(string path, IReadOnlyDictionary<string, string?> parameters)
    {
        var builder = new StringBuilder();
        builder.Append(path);
        var idx = 0;

        foreach (var (key, value) in parameters)
        {
            if (value is null)
            {
                continue;
            }

            builder.Append(idx == 0 ? '?' : '&');
            builder.Append(Uri.EscapeDataString(key));
            builder.Append('=');
            builder.Append(Uri.EscapeDataString(value));
            idx++;
        }

        return idx == 0 ? path : builder.ToString();
    }
}
