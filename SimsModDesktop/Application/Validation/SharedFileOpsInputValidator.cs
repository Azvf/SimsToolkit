using SimsModDesktop.Application.Requests;

namespace SimsModDesktop.Application.Validation;

public sealed class SharedFileOpsInputValidator : IActionInputValidator<SharedFileOpsInput>
{
    public bool TryValidate(SharedFileOpsInput input, out string error)
    {
        error = string.Empty;

        if (input.PrefixHashBytes is int prefixHashBytes &&
            (prefixHashBytes < 1024 || prefixHashBytes > 104857600))
        {
            error = $"Value {prefixHashBytes} is out of range [1024, 104857600].";
            return false;
        }

        if (input.HashWorkerCount is int hashWorkerCount &&
            (hashWorkerCount < 1 || hashWorkerCount > 64))
        {
            error = $"Value {hashWorkerCount} is out of range [1, 64].";
            return false;
        }

        return true;
    }
}
