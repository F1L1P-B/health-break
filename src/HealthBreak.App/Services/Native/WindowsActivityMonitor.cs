using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using HealthBreak.Core.Models;

namespace HealthBreak.App.Services.Native;

/// <summary>Reads only time since input and, when enabled, the foreground process name.</summary>
public sealed class WindowsActivityMonitor
{
    public ActivitySample Sample(bool trackApplications)
    {
        var timestamp = DateTimeOffset.Now;
        var lastInput = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref lastInput) || !IsInteractiveDesktop())
            return new ActivitySample(timestamp, 0, false);

        // LASTINPUTINFO is a 32-bit uptime value. Unsigned subtraction handles its rollover.
        var idleSeconds = unchecked((uint)Environment.TickCount - lastInput.Time) / 1000d;
        if (!trackApplications)
            return new ActivitySample(timestamp, idleSeconds, true);

        var processName = ReadForegroundProcess();
        return new ActivitySample(timestamp, idleSeconds, true, processName,
            processName is null ? null : ProcessCategory.Classify(processName));
    }

    private static bool IsInteractiveDesktop()
    {
        const uint desktopReadObjects = 0x0001;
        var desktop = OpenInputDesktop(0, false, desktopReadObjects);
        if (desktop == IntPtr.Zero)
            return false;

        try
        {
            // Querying whether our desktop receives input never switches or unlocks desktops.
            var ownDesktop = GetThreadDesktop(GetCurrentThreadId());
            if (!GetUserObjectInformation(ownDesktop, 6, out var receivesInput, sizeof(int), out _) || receivesInput == 0)
                return false;

            // A disconnected remote session can still expose its desktop to the process.
            // If Remote Desktop Services is stopped, retain the desktop result as fallback.
            if (!WTSQuerySessionInformation(IntPtr.Zero, uint.MaxValue, 8, out var buffer, out var bytes))
                return true;
            try { return bytes >= sizeof(int) && Marshal.ReadInt32(buffer) == 0; }
            finally { WTSFreeMemory(buffer); }
        }
        finally { CloseDesktop(desktop); }
    }

    private static string? ReadForegroundProcess()
    {
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero || GetWindowThreadProcessId(window, out var processId) == 0 || processId == 0)
            return null;

        try
        {
            using var process = Process.GetProcessById(checked((int)processId));
            return process.ProcessName.ToLowerInvariant() + ".exe";
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or Win32Exception or OverflowException)
        {
            // Foreground processes may terminate or deny access between these two queries.
            return null;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo { public uint Size; public uint Time; }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LastInputInfo lastInput);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr OpenInputDesktop(uint flags, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint desiredAccess);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseDesktop(IntPtr desktop);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetThreadDesktop(uint threadId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", EntryPoint = "GetUserObjectInformationW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetUserObjectInformation(IntPtr handle, int index, out int information, uint length, out uint needed);

    [DllImport("wtsapi32.dll", EntryPoint = "WTSQuerySessionInformationW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSQuerySessionInformation(IntPtr server, uint session, int informationClass, out IntPtr buffer, out uint returnedBytes);

    [DllImport("wtsapi32.dll")]
    private static extern void WTSFreeMemory(IntPtr memory);
}
