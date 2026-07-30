using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace WorkLogAI.App;

public sealed class GlobalHotKey : IDisposable
{
    private const int HotKeyId = 0x5741;
    private const int WmHotKey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;

    private readonly Action _onPressed;
    private readonly HwndSource _source;
    private bool _registered;

    public GlobalHotKey(Action onPressed)
    {
        _onPressed = onPressed;
        var parameters = new HwndSourceParameters("WorkLogAI.HotKey")
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

        var virtualKey = (uint)KeyInterop.VirtualKeyFromKey(Key.W);
        _registered = RegisterHotKey(_source.Handle, HotKeyId, ModControl | ModAlt, virtualKey);
        return _registered;
    }

    public void Dispose()
    {
        if (_registered)
        {
            UnregisterHotKey(_source.Handle, HotKeyId);
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
        if (message == WmHotKey && wParam.ToInt32() == HotKeyId)
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
