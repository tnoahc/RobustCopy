using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using RobustCopy.Core;
using RobustCopy.Services;
using RobustCopy.ViewModels;

namespace RobustCopy;

public partial class MainWindow : Window
{
    private const int DwmUseImmersiveDarkMode = 20;
    private bool _allowClose;
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        var desktopServices = new DesktopServices();
        _viewModel = new MainViewModel(new RobocopyRunner(), desktopServices, desktopServices, desktopServices);
        DataContext = _viewModel;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var enabled = 1;
        _ = DwmSetWindowAttribute(
            new WindowInteropHelper(this).Handle,
            DwmUseImmersiveDarkMode,
            ref enabled,
            Marshal.SizeOf<int>());
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int valueSize);

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
        {
            await DisposeViewModelAsync();
            return;
        }

        if (!_viewModel.IsBusy)
        {
            _allowClose = true;
            await DisposeViewModelAsync();
            return;
        }

        var result = MessageBox.Show(
            $"A scan or copy is still active. Stop it and close {AppIdentity.DisplayName}?",
            "Copy in progress",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (result != MessageBoxResult.Yes)
        {
            e.Cancel = true;
            return;
        }

        e.Cancel = true;
        IsEnabled = false;
        try
        {
            await _viewModel.StopForShutdownAsync();
        }
        catch (Exception exception)
        {
            // Closing must always complete, even if the active job could not be stopped cleanly.
            CrashReporter.Write(exception, "Stopping the active job during shutdown");
        }

        await DisposeViewModelAsync();
        _allowClose = true;
        Close();
    }

    private async Task DisposeViewModelAsync()
    {
        try
        {
            await _viewModel.DisposeAsync();
        }
        catch (Exception exception)
        {
            CrashReporter.Write(exception, "Disposing the main view model during shutdown");
        }
    }
}
