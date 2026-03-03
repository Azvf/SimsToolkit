using SimsModDesktop.Application.Requests;

namespace SimsModDesktop.Application.Validation;

public sealed class NormalizeInputValidator : IActionInputValidator<NormalizeInput>
{
    public bool TryValidate(NormalizeInput input, out string error)
    {
        return ValidationHelpers.ValidateScriptPath(input.ScriptPath, out error);
    }
}
