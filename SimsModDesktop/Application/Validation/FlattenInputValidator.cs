using SimsModDesktop.Application.Requests;

namespace SimsModDesktop.Application.Validation;

public sealed class FlattenInputValidator : IActionInputValidator<FlattenInput>
{
    private readonly IActionInputValidator<SharedFileOpsInput> _sharedValidator;

    public FlattenInputValidator(IActionInputValidator<SharedFileOpsInput> sharedValidator)
    {
        _sharedValidator = sharedValidator;
    }

    public bool TryValidate(FlattenInput input, out string error)
    {
        if (!ValidationHelpers.ValidateScriptPath(input.ScriptPath, out error))
        {
            return false;
        }

        if (!_sharedValidator.TryValidate(input.Shared, out error))
        {
            return false;
        }

        return true;
    }
}
