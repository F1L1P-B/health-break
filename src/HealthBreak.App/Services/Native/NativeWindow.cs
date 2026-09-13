using System.Runtime.InteropServices;

namespace HealthBreak.App.Services.Native;

public static class NativeWindow
{
    public static void Hide(IntPtr window) => ShowWindow(window, 0);

    public static void Show(IntPtr window)
    {
        ShowWindow(window, IsIconic(window) ? 9 : 5);
        SetForegroundWindow(window);
    }

    public static void BringToFront(IntPtr window) => Show(window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);
}

public static class NotificationSound
{
    public static void Play() => MessageBeep(0x00000040);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MessageBeep(uint type);
}
