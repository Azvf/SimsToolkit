using SimsModDesktop.Application.Requests;
using SimsModDesktop.Application.Validation;

namespace SimsModDesktop.Tests;

public sealed class TrayDependenciesInputValidatorTests
{
    [Fact]
    public void Validate_StrictModeWithoutS4tiPath_ReturnsError()
    {
        var scriptPath = Path.GetTempFileName();
        var trayDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        var modsDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));

        try
        {
            var validator = new TrayDependenciesInputValidator();
            var input = new TrayDependenciesInput
            {
                ScriptPath = scriptPath,
                TrayPath = trayDir.FullName,
                ModsPath = modsDir.FullName,
                AnalysisMode = "StrictS4TI"
            };

            var ok = validator.TryValidate(input, out var error);

            Assert.False(ok);
            Assert.Equal("S4TI path is required in StrictS4TI mode.", error);
        }
        finally
        {
            File.Delete(scriptPath);
            trayDir.Delete(recursive: true);
            modsDir.Delete(recursive: true);
        }
    }
}
