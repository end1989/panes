using System.Windows;
using LibVLCSharp.Shared;

namespace PanesLocal;

public partial class App : Application
{
    public static LibVLC LibVLC { get; private set; } = null!;

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
