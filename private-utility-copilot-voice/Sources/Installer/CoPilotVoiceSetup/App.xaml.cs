using System.Windows;
using CoPilotVoiceSetup.Core;

namespace CoPilotVoiceSetup;

public partial class App : System.Windows.Application
{
    private void Application_Startup(object sender, StartupEventArgs e)
    {
        var uninstall = e.Args.Any(a =>
            string.Equals(a, "--uninstall", StringComparison.OrdinalIgnoreCase)
            || string.Equals(a, "/uninstall", StringComparison.OrdinalIgnoreCase));

        var window = new MainWindow(startInUninstallMode: uninstall);
        window.Show();
    }
}
