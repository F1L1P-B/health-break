using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace HealthBreak.App.Services.Native;

public enum TrayAction { OpenDashboard = 1, StartBreak, PauseMonitoring, ResumeMonitoring, Settings, Exit }

/// <summary>A native notification-area icon. Construct, update and dispose on the window's UI thread.</summary>
public sealed class TrayIcon : IDisposable
{
    private const uint CallbackMessage = 0x8000 + 91;
    private const uint IconId = 1;
    private static readonly UIntPtr SubclassId = new(0x4842);
    private readonly IntPtr _window;
    private readonly Action<TrayAction> _onAction;
    private readonly SubclassProcedure _procedure;
    private readonly uint _uiThread;
    private readonly uint _taskbarCreatedMessage;
    private readonly bool _ownsIcon;
    private NotifyIconData _data;
    private bool _disposed;
    private bool _paused;
    private bool _version4;

    public bool IsAvailable { get; private set; }

    public TrayIcon(IntPtr window, Action<TrayAction> onAction)
    {
        if (window == IntPtr.Zero) throw new ArgumentException("A live window handle is required.", nameof(window));
        ArgumentNullException.ThrowIfNull(onAction);
        _window = window;
        _onAction = onAction;
        _procedure = ProcessMessage;
        _uiThread = GetCurrentThreadId();
        if (GetWindowThreadProcessId(window, out _) != _uiThread)
            throw new InvalidOperationException("Create the tray icon on the window's UI thread.");

        _taskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");
        var icon = LoadApplicationIcon(out _ownsIcon);
        _data = new NotifyIconData
        {
            Size = (uint)Marshal.SizeOf<NotifyIconData>(),
            Window = window,
            Id = IconId,
            Flags = 0x1 | 0x2 | 0x4 | 0x80,
            CallbackMessage = CallbackMessage,
            Icon = icon,
            Tip = "HealthBreak · Ready",
            Info = string.Empty,
            InfoTitle = string.Empty
        };
        if (!SetWindowSubclass(window, _procedure, SubclassId, UIntPtr.Zero))
        {
            if (_ownsIcon && icon != IntPtr.Zero) DestroyIcon(icon);
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not create the HealthBreak tray callback.");
        }
        AddIcon();
    }

    public void Update(double activeSeconds, double nextBreakSeconds, bool paused)
    {
        VerifyThread();
        if (_disposed) return;
        _paused = paused;
        var active = double.IsFinite(activeSeconds) ? Math.Max(0, Math.Floor(activeSeconds / 60)) : 0;
        var next = double.IsFinite(nextBreakSeconds) ? Math.Max(0, Math.Ceiling(nextBreakSeconds / 60)) : 0;
        var tip = paused
            ? $"HealthBreak · Monitoring paused\nActive: {active:0} min"
            : $"Active: {active:0} min\nNext recommended break: {next:0} min";
        if (tip.Length > 127) tip = tip[..127];
        if (tip == _data.Tip && IsAvailable) return;
        _data.Tip = tip;
        if (!IsAvailable) AddIcon();
        else if (!ShellNotifyIcon(1, ref _data))
        {
            IsAvailable = false;
            AddIcon();
        }
    }

    private void AddIcon()
    {
        if (_disposed) return;
        IsAvailable = ShellNotifyIcon(0, ref _data);
        _version4 = false;
        if (IsAvailable)
        {
            _data.Version = 4;
            _version4 = ShellNotifyIcon(4, ref _data);
        }
    }

    private IntPtr ProcessMessage(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam, UIntPtr subclassId, UIntPtr reference)
    {
        try
        {
            if (!_disposed && _taskbarCreatedMessage != 0 && message == _taskbarCreatedMessage)
            {
                IsAvailable = false;
                AddIcon();
            }
            else if (!_disposed && message == CallbackMessage)
            {
                var eventId = _version4 ? (uint)(lParam.ToInt64() & 0xffff) : unchecked((uint)lParam.ToInt64());
                if (eventId is 0x0400 or 0x0401 || (!_version4 && eventId == 0x0203))
                    _onAction(TrayAction.OpenDashboard);
                else if (eventId == 0x007B || (!_version4 && eventId == 0x0205))
                    ShowMenu();
                return IntPtr.Zero;
            }
            else if (message == 0x0082) // WM_NCDESTROY
                Dispose();
        }
        catch (Exception error)
        {
            // A managed exception must never unwind into the unmanaged window procedure.
            Debug.WriteLine($"HealthBreak tray callback failed: {error}");
        }
        return DefSubclassProc(window, message, wParam, lParam);
    }

    private void ShowMenu()
    {
        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero) return;
        uint selected;
        try
        {
            AppendMenu(menu, 0, (UIntPtr)TrayAction.OpenDashboard, "Open dashboard");
            AppendMenu(menu, 0, (UIntPtr)TrayAction.StartBreak, "Start break");
            AppendMenu(menu, 0x0800, UIntPtr.Zero, null);
            AppendMenu(menu, _paused ? 0x0001u : 0, (UIntPtr)TrayAction.PauseMonitoring, "Pause monitoring");
            AppendMenu(menu, _paused ? 0 : 0x0001u, (UIntPtr)TrayAction.ResumeMonitoring, "Resume monitoring");
            AppendMenu(menu, 0, (UIntPtr)TrayAction.Settings, "Settings");
            AppendMenu(menu, 0x0800, UIntPtr.Zero, null);
            AppendMenu(menu, 0, (UIntPtr)TrayAction.Exit, "Exit");
            GetCursorPos(out var cursor);
            SetForegroundWindow(_window);
            selected = TrackPopupMenu(menu, 0x0100 | 0x0002, cursor.X, cursor.Y, 0, _window, IntPtr.Zero);
        }
        finally
        {
            DestroyMenu(menu);
            PostMessage(_window, 0, UIntPtr.Zero, IntPtr.Zero);
        }
        if (selected is >= (uint)TrayAction.OpenDashboard and <= (uint)TrayAction.Exit)
            _onAction((TrayAction)selected);
    }

    public void Dispose()
    {
        VerifyThread();
        if (_disposed) return;
        _disposed = true;
        ShellNotifyIcon(2, ref _data);
        IsAvailable = false;
        RemoveWindowSubclass(_window, _procedure, SubclassId);
        if (_ownsIcon && _data.Icon != IntPtr.Zero) DestroyIcon(_data.Icon);
        _data.Icon = IntPtr.Zero;
        GC.KeepAlive(_procedure);
    }

    private void VerifyThread()
    {
        if (GetCurrentThreadId() != _uiThread)
            throw new InvalidOperationException("Update and dispose the tray icon on the window's UI thread.");
    }

    private static IntPtr LoadApplicationIcon(out bool ownsIcon)
    {
        foreach (var name in new[] { "Assets/app.ico", "Assets/HealthBreak.ico", "app.ico" })
        {
            var path = Path.Combine(AppContext.BaseDirectory, name);
            if (!File.Exists(path)) continue;
            var icon = LoadImage(IntPtr.Zero, path, 1, GetSystemMetrics(49), GetSystemMetrics(50), 0x0010);
            if (icon == IntPtr.Zero) continue;
            ownsIcon = true;
            return icon;
        }
        ownsIcon = false;
        return LoadIcon(IntPtr.Zero, new IntPtr(32512));
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size;
        public IntPtr Window;
        public uint Id;
        public uint Flags;
        public uint CallbackMessage;
        public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State;
        public uint StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags;
        public Guid Guid;
        public IntPtr BalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X; public int Y; }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr SubclassProcedure(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam, UIntPtr subclassId, UIntPtr reference);

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellNotifyIcon(uint message, ref NotifyIconData data);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(IntPtr window, SubclassProcedure procedure, UIntPtr subclassId, UIntPtr reference);

    [DllImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(IntPtr window, SubclassProcedure procedure, UIntPtr subclassId);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll", EntryPoint = "RegisterWindowMessageW", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string message);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", EntryPoint = "AppendMenuW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenu(IntPtr menu, uint flags, UIntPtr itemId, string? label);

    [DllImport("user32.dll")]
    private static extern uint TrackPopupMenu(IntPtr menu, uint flags, int x, int y, int reserved, IntPtr window, IntPtr rectangle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "LoadImageW", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadImage(IntPtr instance, string name, uint type, int width, int height, uint flags);

    [DllImport("user32.dll", EntryPoint = "LoadIconW")]
    private static extern IntPtr LoadIcon(IntPtr instance, IntPtr name);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
}
