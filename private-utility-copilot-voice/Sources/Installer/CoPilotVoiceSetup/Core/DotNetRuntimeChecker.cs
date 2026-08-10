namespace CoPilotVoiceSetup.Core;

/// <summary>Detects Microsoft.WindowsDesktop.App 8.x shared framework.</summary>
public static class DotNetRuntimeChecker
{
    public static bool IsDotNet8DesktopRuntimeInstalled(string? programFiles = null, string? programFilesX86 = null)
    {
        programFiles ??= Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        programFilesX86 ??= Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        foreach (var root in new[] { programFiles, programFilesX86 }.Where(r => !string.IsNullOrWhiteSpace(r)))
        {
            var shared = Path.Combine(root!, "dotnet", "shared", "Microsoft.WindowsDesktop.App");
            if (!Directory.Exists(shared))
                continue;
            try
            {
                foreach (var dir in Directory.GetDirectories(shared))
                {
                    var name = Path.GetFileName(dir);
                    if (name.StartsWith("8.", StringComparison.Ordinal))
                        return true;
                }
            }
            catch
            {
                // continue
            }
        }

        return false;
    }
}
