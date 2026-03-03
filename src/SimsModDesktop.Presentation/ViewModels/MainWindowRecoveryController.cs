using SimsModDesktop.Application.Modules;
using SimsModDesktop.Application.Recovery;
using SimsModDesktop.Application.Results;
using SimsModDesktop.Application.Requests;
using SimsModDesktop.TrayDependencyEngine;
using System.Text.Json;

namespace SimsModDesktop.Presentation.ViewModels;

public sealed class MainWindowRecoveryController
{
    private static readonly JsonSerializerOptions RecoveryJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IOperationRecoveryCoordinator? _operationRecoveryCoordinator;
    private readonly IActionResultRepository? _actionResultRepository;

    public MainWindowRecoveryController(
        IOperationRecoveryCoordinator? operationRecoveryCoordinator = null,
        IActionResultRepository? actionResultRepository = null)
    {
        _operationRecoveryCoordinator = operationRecoveryCoordinator;
        _actionResultRepository = actionResultRepository;
    }

    public Task<string?> RegisterRecoveryAsync(RecoverableOperationPayload payload)
    {
        if (_operationRecoveryCoordinator is null)
        {
            return Task.FromResult<string?>(null);
        }

        return _operationRecoveryCoordinator.RegisterPendingAsync(payload)!;
    }

    public Task MarkRecoveryStartedAsync(string? operationId)
    {
        if (_operationRecoveryCoordinator is null || string.IsNullOrWhiteSpace(operationId))
        {
            return Task.CompletedTask;
        }

        return _operationRecoveryCoordinator.MarkStartedAsync(operationId);
    }

    public Task MarkRecoveryCompletedAsync(string? operationId, RecoverableOperationCompletion completion)
    {
        if (_operationRecoveryCoordinator is null || string.IsNullOrWhiteSpace(operationId))
        {
            return Task.CompletedTask;
        }

        return _operationRecoveryCoordinator.MarkCompletedAsync(operationId, completion);
    }

    public Task SaveResultHistoryAsync(SimsAction action, string source, string summary, string? relatedOperationId)
    {
        if (_actionResultRepository is null)
        {
            return Task.CompletedTask;
        }

        return _actionResultRepository.SaveAsync(
            new ActionResultEnvelope
            {
                Action = action,
                Source = source,
                GeneratedAtLocal = DateTime.Now,
                Rows =
                [
                    new ActionResultRow
                    {
                        Name = action.ToString(),
                        Status = summary,
                        RawSummary = summary
                    }
                ]
            },
            relatedOperationId);
    }

    public Task InitializeAsync()
    {
        if (_actionResultRepository is null)
        {
            return Task.CompletedTask;
        }

        return _actionResultRepository.InitializeAsync();
    }

    public RecoverableOperationPayload BuildToolkitRecoveryPayload(CliExecutionPlan plan)
    {
        var input = plan.Input;
        return new RecoverableOperationPayload
        {
            Workspace = AppWorkspace.Toolkit,
            Action = input.Action,
            DisplayTitle = BuildRecoveryTitle(RecoverableOperationLaunchSource.Toolkit, input.Action),
            PayloadKind = "ToolkitCli",
            PayloadJson = JsonSerializer.Serialize(
                new ToolkitCliRecoveryPayload
                {
                    InputType = GetToolkitRecoveryInputType(input),
                    InputJson = JsonSerializer.Serialize(input, input.GetType(), RecoveryJsonOptions)
                },
                RecoveryJsonOptions),
            LaunchSource = RecoverableOperationLaunchSource.Toolkit
        };
    }

    public RecoverableOperationPayload BuildTrayDependenciesRecoveryPayload(TrayDependenciesExecutionPlan plan) =>
        new()
        {
            Workspace = AppWorkspace.Toolkit,
            Action = SimsAction.TrayDependencies,
            DisplayTitle = BuildRecoveryTitle(RecoverableOperationLaunchSource.TrayDependencies, SimsAction.TrayDependencies),
            PayloadKind = "TrayDependencies",
            PayloadJson = JsonSerializer.Serialize(plan.Request, RecoveryJsonOptions),
            LaunchSource = RecoverableOperationLaunchSource.TrayDependencies
        };

    public RecoverableOperationPayload BuildTrayPreviewRecoveryPayload(TrayPreviewInput input) =>
        new()
        {
            Workspace = AppWorkspace.TrayPreview,
            Action = SimsAction.TrayPreview,
            DisplayTitle = BuildRecoveryTitle(RecoverableOperationLaunchSource.TrayPreview, SimsAction.TrayPreview),
            PayloadKind = "TrayPreview",
            PayloadJson = JsonSerializer.Serialize(input, RecoveryJsonOptions),
            LaunchSource = RecoverableOperationLaunchSource.TrayPreview
        };

    public CliExecutionPlan BuildToolkitCliPlan(RecoverableOperationPayload payload)
    {
        var recovery = JsonSerializer.Deserialize<ToolkitCliRecoveryPayload>(payload.PayloadJson, RecoveryJsonOptions)
            ?? throw new InvalidOperationException("Invalid toolkit recovery payload.");

        ISimsExecutionInput input = recovery.InputType switch
        {
            nameof(OrganizeInput) => JsonSerializer.Deserialize<OrganizeInput>(recovery.InputJson, RecoveryJsonOptions)
                ?? throw new InvalidOperationException("Invalid organize recovery payload."),
            nameof(FlattenInput) => JsonSerializer.Deserialize<FlattenInput>(recovery.InputJson, RecoveryJsonOptions)
                ?? throw new InvalidOperationException("Invalid flatten recovery payload."),
            nameof(NormalizeInput) => JsonSerializer.Deserialize<NormalizeInput>(recovery.InputJson, RecoveryJsonOptions)
                ?? throw new InvalidOperationException("Invalid normalize recovery payload."),
            nameof(MergeInput) => JsonSerializer.Deserialize<MergeInput>(recovery.InputJson, RecoveryJsonOptions)
                ?? throw new InvalidOperationException("Invalid merge recovery payload."),
            nameof(FindDupInput) => JsonSerializer.Deserialize<FindDupInput>(recovery.InputJson, RecoveryJsonOptions)
                ?? throw new InvalidOperationException("Invalid find-duplicates recovery payload."),
            _ => throw new InvalidOperationException($"Unsupported toolkit recovery input type: {recovery.InputType}")
        };

        return new CliExecutionPlan(input);
    }

    public TrayDependencyAnalysisRequest BuildTrayDependenciesRequest(RecoverableOperationPayload payload) =>
        JsonSerializer.Deserialize<TrayDependencyAnalysisRequest>(payload.PayloadJson, RecoveryJsonOptions)
        ?? throw new InvalidOperationException("Invalid tray dependencies recovery payload.");

    public TrayPreviewInput BuildTrayPreviewInput(RecoverableOperationPayload payload) =>
        JsonSerializer.Deserialize<TrayPreviewInput>(payload.PayloadJson, RecoveryJsonOptions)
        ?? throw new InvalidOperationException("Invalid tray preview recovery payload.");

    private static string BuildRecoveryTitle(RecoverableOperationLaunchSource launchSource, SimsAction action) =>
        launchSource switch
        {
            RecoverableOperationLaunchSource.Toolkit => $"Resume previous {action} task",
            RecoverableOperationLaunchSource.TrayDependencies => "Resume previous tray dependency analysis",
            RecoverableOperationLaunchSource.TrayPreview => "Resume previous tray preview load",
            _ => "Resume previous task"
        };

    private static string GetToolkitRecoveryInputType(ISimsExecutionInput input) => input.GetType().Name;

    private sealed record ToolkitCliRecoveryPayload
    {
        public string InputType { get; init; } = string.Empty;
        public string InputJson { get; init; } = "{}";
    }
}
