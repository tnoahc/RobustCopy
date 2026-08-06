using System.ComponentModel;
using System.Windows;
using RoboCopyGui.Services;
using RoboCopyGui.ViewModels;

namespace RoboCopyGui;

public partial class MainWindow : Window
{
    private bool _allowClose;
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        var desktopServices = new DesktopServices();
        _viewModel = new MainViewModel(new RobocopyRunner(), desktopServices, desktopServices, desktopServices);
        DataContext = _viewModel;
    }

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
            "A scan or copy is still active. Stop it and close RoboCopy GUI?",
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
