using SimsModDesktop.Application.Requests;

namespace SimsModDesktop.Application.Validation;

public sealed class MergeInputValidator : IActionInputValidator<MergeInput>
{
    private readonly IActionInputValidator<SharedFileOpsInput> _sharedValidator;

    public MergeInputValidator(IActionInputValidator<SharedFileOpsInput> sharedValidator)
    {
        _sharedValidator = sharedValidator;
    }

    public bool TryValidate(MergeInput input, out string error)
    {
        if (!ValidationHelpers.ValidateScriptPath(input.ScriptPath, out error))
        {
            return false;
        }

        if (input.MergeSourcePaths.Count == 0)
        {
            error = "Merge action requires at least one source path.";
            return false;
        }

        if (!_sharedValidator.TryValidate(input.Shared, out error))
        {
            return false;
        }

        return true;
    }
}
