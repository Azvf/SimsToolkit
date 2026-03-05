namespace SimsModDesktop.TrayDependencyEngine;

internal sealed class ModFileCopyPlanner
{
    public ModFileCopyPlan Plan(IReadOnlyList<string> sourceFiles, string targetRoot)
    {
        var plannedItems = new List<ModFileCopyPlanItem>(sourceFiles.Count);
        var issues = new List<TrayDependencyIssue>();
        var seenSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var reservedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (Directory.Exists(targetRoot))
        {
            foreach (var existingPath in Directory.EnumerateFileSystemEntries(targetRoot, "*", SearchOption.TopDirectoryOnly))
            {
                reservedPaths.Add(Path.GetFullPath(existingPath));
            }
        }

        for (var index = 0; index < sourceFiles.Count; index++)
        {
            var rawPath = sourceFiles[index];
            if (string.IsNullOrWhiteSpace(rawPath))
            {
                continue;
            }

            string sourcePath;
            try
            {
                sourcePath = Path.GetFullPath(rawPath);
            }
            catch (Exception ex)
            {
                issues.Add(new TrayDependencyIssue
                {
                    Severity = TrayDependencyIssueSeverity.Warning,
                    Kind = TrayDependencyIssueKind.MissingSourceFile,
                    FilePath = rawPath,
                    Message = $"Source mod file path is invalid: {ex.Message}"
                });
                continue;
            }

            if (!seenSources.Add(sourcePath))
            {
                continue;
            }

            if (!File.Exists(sourcePath))
            {
                issues.Add(new TrayDependencyIssue
                {
                    Severity = TrayDependencyIssueSeverity.Warning,
                    Kind = TrayDependencyIssueKind.MissingSourceFile,
                    FilePath = sourcePath,
                    Message = "Source mod file no longer exists."
                });
                continue;
            }

            var fileName = Path.GetFileName(sourcePath);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                issues.Add(new TrayDependencyIssue
                {
                    Severity = TrayDependencyIssueSeverity.Warning,
                    Kind = TrayDependencyIssueKind.MissingSourceFile,
                    FilePath = sourcePath,
                    Message = "Source mod file path is invalid."
                });
                continue;
            }

            var targetPath = FileNameHelpers.GetUniquePath(Path.Combine(targetRoot, fileName), reservedPaths);
            reservedPaths.Add(targetPath);
            plannedItems.Add(new ModFileCopyPlanItem(sourcePath, targetPath));
        }

        return new ModFileCopyPlan(plannedItems, issues);
    }
}

internal sealed record ModFileCopyPlan(
    IReadOnlyList<ModFileCopyPlanItem> Items,
    IReadOnlyList<TrayDependencyIssue> Issues);

internal sealed record ModFileCopyPlanItem(
    string SourcePath,
    string TargetPath);
