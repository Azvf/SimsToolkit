namespace SimsModDesktop.Application.Validation;

public interface IActionInputValidator<in TInput>
{
    bool TryValidate(TInput input, out string error);
}
