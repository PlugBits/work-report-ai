using Microsoft.Win32;
using WorkLogAI.Core;

namespace WorkLogAI.Infrastructure;

public sealed class WindowsStartupRegistrar : IStartupRegistrar
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "WorkLog AI";

    private readonly string? _executablePath;

    public WindowsStartupRegistrar(string? executablePath = null)
    {
        _executablePath = executablePath ?? Environment.ProcessPath;
    }

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) is string existing && !string.IsNullOrWhiteSpace(existing);
    }

    public void Enable()
    {
        var runValue = ResolveRunValue(_executablePath);
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        key.SetValue(ValueName, runValue);
    }

    public void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    public static string ResolveRunValue(string? processPath)
    {
        if (string.IsNullOrWhiteSpace(processPath))
        {
            throw new InvalidOperationException(
                "自動起動の設定には発行済みのEXEが必要です。dotnet runでは設定できません。");
        }

        var fileName = processPath.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault()
            ?? processPath;
        if (string.Equals(fileName, "dotnet.exe", StringComparison.OrdinalIgnoreCase)
            || string.Equals(fileName, "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "自動起動の設定には発行済みのEXEが必要です。dotnet runでは設定できません。");
        }

        return $"\"{processPath}\"";
    }
}
