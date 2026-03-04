using SimsModDesktop.Application.Requests;

namespace SimsModDesktop.Application.Validation;

public sealed class OrganizeInputValidator : IActionInputValidator<OrganizeInput>
{
    public bool TryValidate(OrganizeInput input, out string error)
    {
        error = string.Empty;
        return true;
    }
}
