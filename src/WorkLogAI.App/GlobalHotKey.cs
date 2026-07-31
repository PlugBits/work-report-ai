using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace WorkLogAI.App;

/// <summary>
/// Registers a single global Ctrl+Alt+&lt;key&gt; hotkey. Modifiers are fixed at
/// Ctrl+Alt; the key and the Win32 hotkey id are supplied by the caller so multiple
/// independent hotkeys (e.g. Ctrl+Alt+W for quick capture, Ctrl+Alt+M for meeting
/// mode) can coexist, each with its own message-only window and id.
/// </summary>
public sealed class GlobalHotKey : IDisposable
{
    private const int WmHotKey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;

    private readonly Key _key;
    private readonly int _hotKeyId;
    private readonly Action _onPressed;
    private readonly HwndSource _source;
    private bool _registered;

    public GlobalHotKey(Key key, int hotKeyId, Action onPressed)
    {
        _key = key;
        _hotKeyId = hotKeyId;
        _onPressed = onPressed;
        var parameters = new HwndSourceParameters($"WorkLogAI.HotKey.{hotKeyId:X}")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WindowProcedure);
    }

    public bool Register()
    {
        if (_registered)
        {
            return true;
        }

        var virtualKey = (uint)KeyInterop.VirtualKeyFromKey(_key);
        _registered = RegisterHotKey(_source.Handle, _hotKeyId, ModControl | ModAlt, virtualKey);
        return _registered;
    }

    public void Dispose()
    {
        if (_registered)
        {
            UnregisterHotKey(_source.Handle, _hotKeyId);
            _registered = false;
        }

        _source.RemoveHook(WindowProcedure);
        _source.Dispose();
    }

    private IntPtr WindowProcedure(
        IntPtr hwnd,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if (message == WmHotKey && wParam.ToInt32() == _hotKeyId)
        {
            handled = true;
            _onPressed();
        }

        return IntPtr.Zero;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(
        IntPtr windowHandle,
        int id,
        uint modifiers,
        uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr windowHandle, int id);
}
