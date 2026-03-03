using SimsModDesktop.Application.Cli;
using SimsModDesktop.Application.Requests;
using SimsModDesktop.Application.Validation;
using SimsModDesktop.Models;

namespace SimsModDesktop.Application.Execution;

public sealed class ActionExecutionStrategy<TInput> : IActionExecutionStrategy
    where TInput : class, ISimsExecutionInput
{
    private readonly IActionInputValidator<TInput> _validator;
    private readonly ISimsCliArgumentBuilder _argumentBuilder;

    public ActionExecutionStrategy(
        SimsAction action,
        IActionInputValidator<TInput> validator,
        ISimsCliArgumentBuilder argumentBuilder)
    {
        Action = action;
        _validator = validator;
        _argumentBuilder = argumentBuilder;
    }

    public SimsAction Action { get; }

    public bool TryValidate(ISimsExecutionInput input, out string error)
    {
        if (input is not TInput typedInput)
        {
            error = $"Execution input type mismatch for action {Action}.";
            return false;
        }

        return _validator.TryValidate(typedInput, out error);
    }

    public SimsProcessCommand BuildCommand(ISimsExecutionInput input)
    {
        if (input is not TInput typedInput)
        {
            throw new InvalidOperationException($"Execution input type mismatch for action {Action}.");
        }

        return _argumentBuilder.Build(typedInput);
    }
}
