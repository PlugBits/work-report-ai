using System.Runtime.InteropServices;

namespace WorkLogAI.App;

/// <summary>
/// Wraps <c>GetLastInputInfo</c> (user32) to report how long the user has gone without any
/// keyboard/mouse input. Used by the reminder tick to detect "just returned from a break" for
/// break-return acceleration. Returns <see cref="TimeSpan.Zero"/> (treated as "not idle", the
/// safe default) if the underlying Win32 call fails.
/// </summary>
public static class IdleTimeProvider
{
    public static TimeSpan GetIdleDuration()
    {
        var info = new LastInputInfo { cbSize = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref info))
        {
            return TimeSpan.Zero;
        }

        // Both dwTime and Environment.TickCount are 32-bit millisecond counters that wrap
        // every ~49.7 days; unsigned subtraction is correct across the wraparound as long as
        // the actual elapsed time is under ~24.8 days, which idle time always is in practice.
        var idleMilliseconds = unchecked((uint)Environment.TickCount - info.dwTime);
        return TimeSpan.FromMilliseconds(idleMilliseconds);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);
}
