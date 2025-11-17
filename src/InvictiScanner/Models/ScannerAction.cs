namespace InvictiScanner.Models;

public enum ScannerAction
{
    Scan,
    Issues
}

public static class ScannerActionExtensions
{
    public static ScannerAction Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return ScannerAction.Scan;
        }

        return Enum.TryParse<ScannerAction>(value, true, out var parsed)
            ? parsed
            : ScannerAction.Scan;
    }
}
