using Microsoft.Win32;

namespace HealthBreak.App.Services.Native;

public sealed class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "HealthBreak";

    public bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            return key?.GetValue(ValueName) is string command && !string.IsNullOrWhiteSpace(command);
        }
    }

    public void SetEnabled(bool enabled)
    {
        if (!enabled)
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }

        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable) ||
            !string.Equals(Path.GetExtension(executable), ".exe", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Path.GetFileName(executable), "dotnet.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Start HealthBreak using its .exe before enabling Windows startup.");

        using var runKey = Registry.CurrentUser.CreateSubKey(RunKey, writable: true)
            ?? throw new IOException("Could not open the current user's Windows startup settings.");
        runKey.SetValue(ValueName, $"\"{executable}\" --background", RegistryValueKind.String);
    }
}
