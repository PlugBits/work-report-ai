using System.Diagnostics;
using System.Windows;
using WorkLogAI.Infrastructure;

namespace WorkLogAI.App;

internal static class ExportResultPrompt
{
    public static void OfferToOpen(Window owner, string path)
    {
        var answer = MessageBox.Show(
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
            MessageBox.Show(
                owner,
                $"ファイルを開けませんでした。\n{exception.Message}",
                "WorkLog AI",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}
