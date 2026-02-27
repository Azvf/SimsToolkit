using SimsModDesktop.Application.Cli;
using SimsModDesktop.Application.Requests;
using SimsModDesktop.Application.Validation;
using SimsModDesktop.Models;
using SimsModDesktop.Services;

namespace SimsModDesktop.Application.Execution;

public sealed class ExecutionCoordinator : IExecutionCoordinator
{
    private readonly ISimsCliArgumentBuilder _argumentBuilder;
    private readonly ISimsPowerShellRunner _runner;
    private readonly IActionInputValidator<OrganizeInput> _organizeValidator;
    private readonly IActionInputValidator<FlattenInput> _flattenValidator;
    private readonly IActionInputValidator<NormalizeInput> _normalizeValidator;
    private readonly IActionInputValidator<MergeInput> _mergeValidator;
    private readonly IActionInputValidator<FindDupInput> _findDupValidator;
    private readonly IActionInputValidator<TrayDependenciesInput> _trayDependenciesValidator;

    public ExecutionCoordinator(
        ISimsCliArgumentBuilder argumentBuilder,
        ISimsPowerShellRunner runner,
        IActionInputValidator<OrganizeInput> organizeValidator,
        IActionInputValidator<FlattenInput> flattenValidator,
        IActionInputValidator<NormalizeInput> normalizeValidator,
        IActionInputValidator<MergeInput> mergeValidator,
        IActionInputValidator<FindDupInput> findDupValidator,
        IActionInputValidator<TrayDependenciesInput> trayDependenciesValidator)
    {
        _argumentBuilder = argumentBuilder;
        _runner = runner;
        _organizeValidator = organizeValidator;
        _flattenValidator = flattenValidator;
        _normalizeValidator = normalizeValidator;
        _mergeValidator = mergeValidator;
        _findDupValidator = findDupValidator;
        _trayDependenciesValidator = trayDependenciesValidator;
    }

    public async Task<SimsExecutionResult> ExecuteAsync(
        ISimsExecutionInput input,
        Action<string> onOutput,
        Action<SimsProgressUpdate>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(onOutput);

        if (!TryValidate(input, out var validationError))
        {
            throw new InvalidOperationException(validationError);
        }

        var command = _argumentBuilder.Build(input);
        return await _runner.RunAsync(command, onOutput, onProgress, cancellationToken);
    }

    private bool TryValidate(ISimsExecutionInput input, out string error)
    {
        switch (input)
        {
            case OrganizeInput organize:
                return _organizeValidator.TryValidate(organize, out error);
            case FlattenInput flatten:
                return _flattenValidator.TryValidate(flatten, out error);
            case NormalizeInput normalize:
                return _normalizeValidator.TryValidate(normalize, out error);
            case MergeInput merge:
                return _mergeValidator.TryValidate(merge, out error);
            case FindDupInput findDup:
                return _findDupValidator.TryValidate(findDup, out error);
            case TrayDependenciesInput trayDependencies:
                return _trayDependenciesValidator.TryValidate(trayDependencies, out error);
            default:
                error = $"Unsupported action input: {input.Action}.";
                return false;
        }
    }
}
