using SimsModDesktop.Application.Requests;

namespace SimsModDesktop.Application.Validation;

public sealed class TrayPreviewInputValidator : IActionInputValidator<TrayPreviewInput>
{
    public bool TryValidate(TrayPreviewInput input, out string error)
    {
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(input.TrayPath))
        {
            error = "TrayPath is required for tray preview.";
            return false;
        }

        if (!Directory.Exists(input.TrayPath))
        {
            error = "TrayPath does not exist.";
            return false;
        }

        if (input.TopN is int topN && (topN < 1 || topN > 50000))
        {
            error = $"Value {topN} is out of range [1, 50000].";
            return false;
        }

        if (input.MaxFilesPerItem < 1 || input.MaxFilesPerItem > 200)
        {
            error = $"Value {input.MaxFilesPerItem} is out of range [1, 200].";
            return false;
        }

        return true;
    }
}
