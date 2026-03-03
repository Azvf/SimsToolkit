namespace SimsModDesktop.Application.Execution;

public sealed class ValidationResult
{
    public bool IsValid { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public static ValidationResult Success() => new() { IsValid = true };

    public static ValidationResult Failure(IEnumerable<string> errors, IEnumerable<string>? warnings = null) => new()
    {
        IsValid = false,
        Errors = errors.ToArray(),
        Warnings = warnings?.ToArray() ?? Array.Empty<string>()
    };
}

public enum TransformationMode
{
    Flatten,
    Normalize,
    Merge,
    Organize
}
