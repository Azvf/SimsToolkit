namespace SimsModDesktop.Application.Execution;

public enum ConflictResolutionStrategy
{
    Prompt,
    Skip,
    Overwrite,
    KeepNewer,
    KeepOlder,
    HashCompare
}
