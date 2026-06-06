namespace PanesLocal.Models;

public class AppState
{
    public WindowState Window { get; set; } = new();
    public GridState Grid { get; set; } = new();
    public int Zoom { get; set; } = 100;
    public PanelState Panel { get; set; } = new();
    public TransportState Transport { get; set; } = new();
    public AudioState Audio { get; set; } = new();
    public List<string> Folders { get; set; } = new();
    public List<string> Queue { get; set; } = new();
    public List<string> Played { get; set; } = new();
    public List<PaneState> Panes { get; set; } = new();

    public class WindowState { public double X { get; set; } = 100; public double Y { get; set; } = 80;
                               public double Width { get; set; } = 1280; public double Height { get; set; } = 720;
                               public bool Maximized { get; set; } public bool Fullscreen { get; set; } }
    public class GridState { public int Cols { get; set; } = 2; public int Rows { get; set; } = 2; }
    public class PanelState { public DockSide Dock { get; set; } = DockSide.Right; public double Size { get; set; } = 320; }
    public class TransportState { public bool Shuffle { get; set; } = true; public bool RepeatAll { get; set; } = true; }
    public class AudioState { public int? UnmutedPaneIndex { get; set; } }
}
