using System.Windows;
using System.Windows.Threading;
using RobustCopy.Core;

namespace RobustCopy;

public partial class App : Application
{
    private bool _isExiting;

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _isExiting = true;
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var reportPath = CrashReporter.Write(e.Exception, "Dispatcher");
        e.Handled = true;

        // During shutdown there is nothing left to keep running, so log quietly and let it finish.
        if (_isExiting || Dispatcher.HasShutdownStarted)
        {
            return;
        }

        try
        {
            var detail = reportPath is null
                ? "A crash report could not be written."
                : $"Details were saved to:{Environment.NewLine}{reportPath}";
            MessageBox.Show(
                $"{AppIdentity.DisplayName} hit an unexpected error and recovered.{Environment.NewLine}{Environment.NewLine}{detail}",
                "Unexpected error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch (Exception)
        {
            // The report is already on disk; a failed notification must not raise a second fault.
        }
    }

    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e) =>
        CrashReporter.Write(e.ExceptionObject, e.IsTerminating ? "AppDomain (terminating)" : "AppDomain");

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        CrashReporter.Write(e.Exception, "Unobserved task");
        e.SetObserved();
    }
}
