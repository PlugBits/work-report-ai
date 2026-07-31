using System.Diagnostics;
using System.Windows;
using WorkLogAI.Infrastructure;

namespace WorkLogAI.App;

internal static class ExportResultPrompt
{
    public static void OfferToOpen(Window owner, string path) => OfferToOpen(path, owner);

    /// <summary>
    /// Owner-less overload for flows with no natural parent window (the tray menu
    /// itself is not a <see cref="Window"/>), matching the owner-less
    /// <see cref="MessageBox.Show(string, string, MessageBoxButton, MessageBoxImage)"/>
    /// pattern already used by <c>App.xaml.cs</c>'s other tray-triggered prompts.
    /// </summary>
    public static void OfferToOpen(string path) => OfferToOpen(path, owner: null);

    private static void OfferToOpen(string path, Window? owner)
    {
        var answer = owner is null
            ? MessageBox.Show(
                $"出力しました:\n{path}\nファイルを開きますか？",
                "WorkLog AI",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information)
            : MessageBox.Show(
                owner,
                $"出力しました:\n{path}\nファイルを開きますか？",
                "WorkLog AI",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            ErrorLog.Log("ExportResultPrompt.Open", exception);
            var message = $"ファイルを開けませんでした。\n{exception.Message}";
            if (owner is null)
            {
                MessageBox.Show(message, "WorkLog AI", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            else
            {
                MessageBox.Show(owner, message, "WorkLog AI", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
