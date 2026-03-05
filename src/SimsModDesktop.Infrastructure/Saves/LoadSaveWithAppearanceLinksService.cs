using SimsModDesktop.PackageCore;
using SimsModDesktop.SaveData.Services;

namespace SimsModDesktop.Infrastructure.Saves;

public sealed class LoadSaveWithAppearanceLinksService : ILoadSaveWithAppearanceLinksService
{
    private readonly ISaveAppearanceLinkService _saveAppearanceLinkService;

    public LoadSaveWithAppearanceLinksService(ISaveAppearanceLinkService saveAppearanceLinkService)
    {
        _saveAppearanceLinkService = saveAppearanceLinkService;
    }

    public async Task<LoadSaveWithAppearanceLinksResult> LoadAsync(
        LoadSaveWithAppearanceLinksRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.SavePath))
        {
            return new LoadSaveWithAppearanceLinksResult
            {
                Success = false,
                Error = "Save path is required.",
                Issues =
                [
                    BuildErrorIssue("INVALID_REQUEST", "Save path is required.")
                ]
            };
        }

        try
        {
            var snapshot = await _saveAppearanceLinkService
                .BuildSnapshotAsync(
                    request.SavePath,
                    request.GameRoot ?? string.Empty,
                    request.ModsRoot ?? string.Empty,
                    cancellationToken)
                .ConfigureAwait(false);

            return new LoadSaveWithAppearanceLinksResult
            {
                Success = true,
                Snapshot = snapshot,
                Issues = snapshot.Issues
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (IsIoException(ex))
        {
            return new LoadSaveWithAppearanceLinksResult
            {
                Success = false,
                Error = ex.Message,
                Issues =
                [
                    BuildErrorIssue("LOAD_FAILED", ex.Message)
                ]
            };
        }
        catch (Exception ex)
        {
            return new LoadSaveWithAppearanceLinksResult
            {
                Success = false,
                Error = ex.Message,
                Issues =
                [
                    BuildErrorIssue("UNEXPECTED_ERROR", ex.Message)
                ]
            };
        }
    }

    private static bool IsIoException(Exception ex)
    {
        return ex is IOException or UnauthorizedAccessException or InvalidDataException;
    }

    private static Ts4AppearanceIssue BuildErrorIssue(string code, string message)
    {
        return new Ts4AppearanceIssue
        {
            Code = code,
            Severity = Ts4AppearanceIssueSeverity.Error,
            Scope = Ts4AppearanceIssueScope.Snapshot,
            Message = message
        };
    }
}
