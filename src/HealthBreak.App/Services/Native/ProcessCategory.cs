namespace HealthBreak.App.Services.Native;

public static class ProcessCategory
{
    public static string Classify(string processName) => Path.GetFileNameWithoutExtension(processName).ToLowerInvariant() switch
    {
        "chrome" or "firefox" or "msedge" or "opera" or "brave" or "vivaldi" => "Browser",
        "code" or "devenv" or "rider64" or "idea64" or "pycharm64" or "webstorm64" or "windowsterminal" or "powershell" or "pwsh" => "Development",
        "discord" or "slack" or "teams" or "ms-teams" or "zoom" or "telegram" or "signal" => "Communication",
        "steam" or "epicgameslauncher" or "battle.net" or "goggalaxy" or "riotclientservices" => "Gaming",
        "spotify" or "vlc" or "wmplayer" or "music.ui" or "netflix" => "Entertainment",
        "winword" or "excel" or "powerpnt" or "outlook" or "onenote" or "acrobat" or "acrord32" or "notion" => "Work",
        _ => "Other"
    };
}
