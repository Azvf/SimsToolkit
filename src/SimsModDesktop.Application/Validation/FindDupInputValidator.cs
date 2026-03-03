using SimsModDesktop.Application.Requests;

namespace SimsModDesktop.Application.Validation;

public sealed class FindDupInputValidator : IActionInputValidator<FindDupInput>
{
    private readonly IActionInputValidator<SharedFileOpsInput> _sharedValidator;

    public FindDupInputValidator(IActionInputValidator<SharedFileOpsInput> sharedValidator)
    {
        _sharedValidator = sharedValidator;
    }

    public bool TryValidate(FindDupInput input, out string error)
    {
        if (!ValidationHelpers.ValidateScriptPath(input.ScriptPath, out error))
        {
            return false;
        }

        var rootPath = ValidationHelpers.ToNullIfWhiteSpace(input.FindDupRootPath);
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            error = "FindDuplicates requires RootPath.";
            return false;
        }

        if (!Directory.Exists(rootPath))
        {
            error = "FindDuplicates RootPath does not exist.";
            return false;
        }

        if (!_sharedValidator.TryValidate(input.Shared, out error))
        {
            return false;
        }

        return true;
    }
}
