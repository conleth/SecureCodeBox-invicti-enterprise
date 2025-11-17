namespace InvictiScanner.Models;

public sealed record SecureCodeBoxFinding
{
    public string Name { get; init; } = string.Empty;
    public string Category { get; init; } = "Invicti Finding";
    public string Severity { get; init; } = "medium";
    public string Description { get; init; } = string.Empty;
    public string? Location { get; init; }
    public string? AttributeIdentifier { get; init; }
    public IReadOnlyDictionary<string, object?> Attributes { get; init; } = new Dictionary<string, object?>();
}
