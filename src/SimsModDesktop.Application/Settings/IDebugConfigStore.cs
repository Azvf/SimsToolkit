namespace SimsModDesktop.Application.Settings;

public sealed record DebugConfigTemplateEntry(
    string Key,
    string DefaultValue,
    string Description);

public interface IDebugConfigStore
{
    Task<IReadOnlyDictionary<string, string>> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(IReadOnlyDictionary<string, string> entries, CancellationToken cancellationToken = default);
    Task EnsureTemplateAsync(
        IReadOnlyList<DebugConfigTemplateEntry> entries,
        CancellationToken cancellationToken = default);
}
