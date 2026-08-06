using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace RoboCopyGui.Services;

public interface IFolderPicker
{
    string? PickFolder(string title, string initialFolder);
}

public interface IUserDialogService
{
    void ShowError(string title, string message);
    void ShowInformation(string title, string message);
    bool ConfirmDestructive(string destination, int plannedDeletes, bool movesSourceItems);
}

public interface IDesktopLauncher
{
    void OpenPath(string path);
    void CopyText(string text);
}

public sealed class DesktopServices : IFolderPicker, IUserDialogService, IDesktopLauncher
{
    public string? PickFolder(string title, string initialFolder)
    {
        var dialog = new OpenFolderDialog
        {
            Title = title,
            Multiselect = false,
            InitialDirectory = Directory.Exists(initialFolder) ? initialFolder : string.Empty
        };
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    public void ShowError(string title, string message) =>
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);

    public void ShowInformation(string title, string message) =>
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public bool ConfirmDestructive(string destination, int plannedDeletes, bool movesSourceItems)
    {
        var effects = new List<string>();
        if (plannedDeletes > 0)
        {
            effects.Add($"• Up to {plannedDeletes:N0} extra destination item(s) are scheduled for deletion.");
        }

        if (movesSourceItems)
        {
            effects.Add("• Successfully copied source items will be deleted from the source.");
        }

        if (effects.Count == 0)
        {
            effects.Add("• This mode is allowed to delete or move data even if the pre-scan found no current deletions.");
        }

        var message = $"Review this destructive operation carefully.\n\nDestination:\n{destination}\n\n{string.Join(Environment.NewLine, effects)}\n\nContinue?";
        return MessageBox.Show(message, "Confirm destructive operation", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
    }

    public void OpenPath(string path)
    {
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    public void CopyText(string text) => Clipboard.SetText(text);
}
