using System.Diagnostics;
using Microsoft.UI.Windowing;

namespace HealthBreak.App.Services;

public static class AppBranding
{
    public static void ApplyIcon(AppWindow window)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "HealthBreak.ico");
        if (!File.Exists(path)) return;
        try { window.SetIcon(path); }
        catch (Exception error) { Debug.WriteLine($"Could not apply the HealthBreak window icon: {error}"); }
    }
}
