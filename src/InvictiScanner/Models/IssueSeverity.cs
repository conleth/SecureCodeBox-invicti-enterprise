namespace InvictiScanner.Models;

public enum IssueSeverity
{
    BestPractice = 0,
    Information = 1,
    Low = 2,
    Medium = 3,
    High = 4,
    Critical = 5
}

public static class IssueSeverityExtensions
{
    public static bool IsAtLeast(this IssueSeverity severity, IssueSeverity minimum) => severity >= minimum;

    public static IssueSeverity Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return IssueSeverity.Medium;
        }

        return Enum.TryParse<IssueSeverity>(value, true, out var parsed)
            ? parsed
            : IssueSeverity.Medium;
    }
}
