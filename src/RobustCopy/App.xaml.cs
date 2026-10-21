using System.Windows;
using RobustCopy.Core;

namespace RobustCopy;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Retention runs once per launch, off the UI thread, and never throws.
        _ = Task.Run(() => AppIdentity.PruneOldLogs(AppIdentity.LogDirectory, DateTime.UtcNow));
    }
}
