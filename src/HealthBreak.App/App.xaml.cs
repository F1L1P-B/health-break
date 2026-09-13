using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.UI.Xaml;

namespace HealthBreak.App;

public partial class App : Application
{
    private Window? _window;
    private Mutex? _instance;
    private string? _diagnosticDirectory;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, args) =>
        {
            try
            {
                var path = _diagnosticDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"HealthBreak");
                Directory.CreateDirectory(path);
                File.AppendAllText(Path.Combine(path,"startup.log"),DateTimeOffset.Now + " " + args.Exception + Environment.NewLine);
            }
            catch { }
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var arguments = Environment.GetCommandLineArgs();
        var index = Array.IndexOf(arguments,"--data-dir");
        var directory = index >= 0 && index+1 < arguments.Length
            ? Path.GetFullPath(arguments[index+1])
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"HealthBreak");
        _diagnosticDirectory = directory;
        var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(directory.ToUpperInvariant())))[..16];
        _instance = new Mutex(true,"Local\\HealthBreak_" + id,out var firstInstance);
        if (!firstInstance)
        {
            MessageBox(IntPtr.Zero,"HealthBreak is already running. Open it from the system tray.","HealthBreak",0x40);
            Exit();
            return;
        }
        try
        {
            _window = new MainWindow(Path.Combine(directory,"healthbreak.db"), arguments.Contains("--background"),
                arguments.Contains("--smoke-test") ? directory : null);
            _window.Closed += (_, _) => { _instance.ReleaseMutex(); _instance.Dispose(); _instance = null; };
            _window.Activate();
        }
        catch (Exception ex)
        {
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory,"startup.log"),ex + Environment.NewLine);
            MessageBox(IntPtr.Zero,"HealthBreak could not start. See startup.log in " + directory + "\n\n" + ex.Message,"HealthBreak",0x10);
            Exit();
        }
    }

    [DllImport("user32.dll",EntryPoint="MessageBoxW",CharSet=CharSet.Unicode)]
    private static extern int MessageBox(IntPtr hwnd,string text,string caption,uint type);
}

