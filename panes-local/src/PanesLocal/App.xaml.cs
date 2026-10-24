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
        try
        {
            Core.Initialize();
            // --avcodec-hw=none forces software decoding to avoid hardware-decode
            // contention between multiple simultaneous panes (was causing freezes).
            LibVLC = new LibVLC(
                "--no-osd",
                "--quiet",
                "--no-video-title-show",
                "--avcodec-hw=none");
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to initialize libVLC.\n\n{ex.GetType().Name}: {ex.Message}\n\n" +
                "Reinstall the app, or copy the contents of the bundled libvlc folder next to PanesLocal.exe.",
                "Panes Local — startup error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        LibVLC?.Dispose();
        base.OnExit(e);
    }
}
