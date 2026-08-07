using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using RoboCopyGui.Core;
using RoboCopyGui.Services;
using RoboCopyGui.ViewModels;

namespace RoboCopyGui;

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
            await _viewModel.DisposeAsync();
            return;
        }

        if (!_viewModel.IsBusy)
        {
            _allowClose = true;
            await _viewModel.DisposeAsync();
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
        await _viewModel.StopForShutdownAsync();
        await _viewModel.DisposeAsync();
        _allowClose = true;
        Close();
    }
}
