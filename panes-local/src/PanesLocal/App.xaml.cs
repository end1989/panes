using System.IO;
using System.Windows;
using LibVLCSharp.Shared;

namespace PanesLocal;

public partial class App : Application
{
    public static LibVLC LibVLC { get; private set; } = null!;
    public static string StateFilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PanesLocal", "state.json");

    protected override void OnStartup(StartupEventArgs e)
    {
        Core.Initialize();
        LibVLC = new LibVLC("--no-osd", "--quiet", "--no-video-title-show");
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        LibVLC?.Dispose();
        base.OnExit(e);
    }
}
