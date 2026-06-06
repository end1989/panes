using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using PanesLocal.Models;
using PanesLocal.Services;
using PanesLocal.Views;

namespace PanesLocal;

public partial class MainWindow : Window
{
    private readonly SharedQueue _queue = new();
    private readonly AudioCoordinator _audio = new();
    private readonly FolderScanner _scanner = new();
    private readonly List<Pane> _panes = new();
    private readonly DispatcherTimer _idleTimer;
    private readonly DispatcherTimer _panelTimer;
    private DateTime _lastMouseMove = DateTime.Now;
    private DateTime _mouseLeftPanelAt = DateTime.MaxValue;
    private bool _panelOpen;
    private bool _panelAnchored;
    private int _cols = 2, _rows = 2, _zoom = 100;
    private bool _shuffleOn = true, _repeatAllOn = true;
    private const double PanelWidth = 320;
    private DockSide _dock = DockSide.Right;
    private bool _draggingPanel;
    private Point _dragStartPos;

    public MainWindow()
    {
        InitializeComponent();
        KeyDown += OnKeyDown;
        MouseMove += OnMouseMove;
        Panel.MouseEnter += (_, _) => _mouseLeftPanelAt = DateTime.MaxValue;
        Panel.MouseLeave += (_, _) => _mouseLeftPanelAt = DateTime.Now;
        Panel.PreviewMouseLeftButtonDown += (_, _) => _panelAnchored = true;

        _idleTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _idleTimer.Tick += (_, _) =>
        {
            if ((DateTime.Now - _lastMouseMove).TotalSeconds > 3) Cursor = Cursors.None;
        };
        _idleTimer.Start();

        _panelTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _panelTimer.Tick += (_, _) => UpdatePanelState();
        _panelTimer.Start();

        Panel.Attach(_queue, _audio);
        Panel.SetGrid(_cols, _rows);
        Panel.SetZoom(_zoom);
        Panel.SetShuffle(_shuffleOn);
        Panel.SetRepeatAll(_repeatAllOn);

        Panel.GridSizeChanged += (c, r) => { _cols = c; _rows = r; BuildGrid(); };
        Panel.ZoomChanged += z => { _zoom = z; ApplyZoom(); };
        Panel.FolderChosen += async p => await LoadFolderAsync(p, replace: true);
        Panel.FolderAdded  += async p => await LoadFolderAsync(p, replace: false);
        Panel.QueueCleared += () => { _queue.Clear(); foreach (var pn in _panes) pn.StopAndBlank(); };
        Panel.ShuffleToggled += on => _shuffleOn = on;
        Panel.RepeatAllToggled += on => _repeatAllOn = on;
        Panel.ReshuffleClicked += () => _queue.Reshuffle();
        Panel.PaneDroppedOnPlaylist += OnPaneDroppedOnPlaylist;

        Panel.Handle.MouseLeftButtonDown += (_, e) =>
        {
            _draggingPanel = true;
            _dragStartPos = e.GetPosition(this);
            Panel.Handle.CaptureMouse();
        };
        Panel.Handle.MouseMove += (_, _) => { /* live-detach effect could go here */ };
        Panel.Handle.MouseLeftButtonUp += (_, e) =>
        {
            if (!_draggingPanel) return;
            _draggingPanel = false;
            Panel.Handle.ReleaseMouseCapture();
            var p = e.GetPosition(this);
            var newDock = ResolveDockFromPosition(p);
            ApplyDock(newDock);
        };

        BuildGrid();
        ClosePanelInstant();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        _lastMouseMove = DateTime.Now;
        Cursor = Cursors.Arrow;
        var p = e.GetPosition(this);
        bool nearEdge = _dock switch
        {
            DockSide.Right  => ActualWidth - p.X < 24,
            DockSide.Left   => p.X < 24,
            DockSide.Top    => p.Y < 24,
            DockSide.Bottom => ActualHeight - p.Y < 24,
            _ => false
        };
        if (nearEdge && !_panelOpen) OpenChevron.Opacity = 0.7;
        else if (!IsMouseOver(OpenChevron, e)) OpenChevron.Opacity = 0.25;
    }

    private bool IsMouseOver(UIElement el, MouseEventArgs e)
    {
        var pos = e.GetPosition(el);
        return pos.X >= 0 && pos.Y >= 0 && pos.X <= el.RenderSize.Width && pos.Y <= el.RenderSize.Height;
    }

    private void UpdatePanelState()
    {
        if (!_panelOpen) return;
        if (_panelAnchored) return;
        if (_mouseLeftPanelAt == DateTime.MaxValue) return;
        if ((DateTime.Now - _mouseLeftPanelAt).TotalMilliseconds > 600)
            ClosePanel();
    }

    private void OpenPanel()
    {
        _panelOpen = true;
        OpenChevron.Visibility = Visibility.Collapsed;
        AnimatePanel(0);
    }

    private void ClosePanel()
    {
        _panelOpen = false;
        _panelAnchored = false;
        _mouseLeftPanelAt = DateTime.MaxValue;
        OpenChevron.Visibility = Visibility.Visible;
        AnimatePanel(_dock switch
        {
            DockSide.Right  =>  PanelWidth,
            DockSide.Left   => -PanelWidth,
            DockSide.Bottom =>  PanelWidth,
            DockSide.Top    => -PanelWidth,
            _ => PanelWidth
        });
    }

    private void ClosePanelInstant()
    {
        _panelOpen = false;
        PanelTransform.X = PanelWidth;
        OpenChevron.Visibility = Visibility.Visible;
    }

    private void AnimatePanel(double target)
    {
        var prop = (_dock is DockSide.Left or DockSide.Right)
            ? TranslateTransform.XProperty
            : TranslateTransform.YProperty;
        var anim = new DoubleAnimation(target, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        PanelTransform.BeginAnimation(prop, anim);
    }

    private DockSide ResolveDockFromPosition(Point p)
    {
        const double edge = 80;
        if (p.X < edge) return DockSide.Left;
        if (ActualWidth - p.X < edge) return DockSide.Right;
        if (p.Y < edge) return DockSide.Top;
        if (ActualHeight - p.Y < edge) return DockSide.Bottom;
        return _dock;  // no edge in range → snap back to previous side
    }

    private void ApplyDock(DockSide dock)
    {
        _dock = dock;
        PanelHost.HorizontalAlignment = dock switch
        {
            DockSide.Left  => HorizontalAlignment.Left,
            DockSide.Right => HorizontalAlignment.Right,
            _ => HorizontalAlignment.Stretch
        };
        PanelHost.VerticalAlignment = dock switch
        {
            DockSide.Top    => VerticalAlignment.Top,
            DockSide.Bottom => VerticalAlignment.Bottom,
            _ => VerticalAlignment.Stretch
        };
        Panel.Width  = (dock is DockSide.Left  or DockSide.Right)  ? PanelWidth : double.NaN;
        Panel.Height = (dock is DockSide.Top   or DockSide.Bottom) ? PanelWidth : double.NaN;

        // Re-orient chevron
        OpenChevron.HorizontalAlignment = dock switch
        {
            DockSide.Left  => HorizontalAlignment.Left,
            DockSide.Right => HorizontalAlignment.Right,
            _ => HorizontalAlignment.Center
        };
        OpenChevron.VerticalAlignment = dock switch
        {
            DockSide.Top    => VerticalAlignment.Top,
            DockSide.Bottom => VerticalAlignment.Bottom,
            _ => VerticalAlignment.Center
        };
        ChevronText.Text = dock switch
        {
            DockSide.Left  => "›",
            DockSide.Right => "‹",
            DockSide.Top   => "˅",
            DockSide.Bottom => "˄",
            _ => "‹"
        };

        // Reset transform — close instantly in new dock
        PanelTransform.X = dock switch
        {
            DockSide.Right => PanelWidth,
            DockSide.Left  => -PanelWidth,
            _ => 0
        };
        PanelTransform.Y = dock switch
        {
            DockSide.Bottom => PanelWidth,
            DockSide.Top    => -PanelWidth,
            _ => 0
        };
        _panelOpen = false;
        OpenChevron.Visibility = Visibility.Visible;
    }

    private void OpenChevron_MouseEnter(object s, MouseEventArgs e) => OpenChevron.Opacity = 0.7;
    private void OpenChevron_MouseLeave(object s, MouseEventArgs e) => OpenChevron.Opacity = 0.25;
    private void OpenChevron_Click(object s, MouseButtonEventArgs e) => OpenPanel();

    private async Task LoadFolderAsync(string path, bool replace)
    {
        if (replace) { _queue.Clear(); foreach (var p in _panes) p.StopAndBlank(); }
        var items = await _scanner.ScanAsync(path, progress: null, CancellationToken.None);
        _queue.AddAll(items);
        if (_shuffleOn) _queue.Shuffle();
        FillIdlePanesFromQueue();
    }

    private void BuildGrid()
    {
        foreach (var p in _panes) p.Dispose();
        _panes.Clear();
        PaneGrid.Children.Clear();
        PaneGrid.RowDefinitions.Clear();
        PaneGrid.ColumnDefinitions.Clear();

        for (int c = 0; c < _cols; c++)
            PaneGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int r = 0; r < _rows; r++)
            PaneGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        int idx = 0;
        for (int r = 0; r < _rows; r++)
            for (int c = 0; c < _cols; c++)
            {
                var pane = new Pane { PaneIndex = idx };
                pane.Attach(_queue, _audio);
                pane.RequestNext   += OnPaneRequestsNext;
                pane.RequestUnmute += p => _audio.UnmutePane(p.PaneIndex);
                pane.RequestMute   += p => _audio.MutePane(p.PaneIndex);
                pane.PlaylistItemDroppedOnMe += OnPlaylistDroppedOnPane;
                pane.PaneDroppedOnMe += OnPaneDroppedOnPane;
                Grid.SetColumn(pane, c);
                Grid.SetRow(pane, r);
                PaneGrid.Children.Add(pane);
                _panes.Add(pane);
                idx++;
            }

        ApplyZoom();
        FillIdlePanesFromQueue();
    }

    private void ApplyZoom()
    {
        var scale = _zoom / 100.0;
        foreach (var pane in _panes)
            pane.RenderTransform = new ScaleTransform(scale, scale, 0.5, 0.5);
    }

    private void FillIdlePanesFromQueue()
    {
        foreach (var p in _panes)
        {
            if (p.Current is not null) continue;
            var next = _queue.TakeNext();
            if (next is null) break;
            p.Play(next);
        }
    }

    private void OnPaneRequestsNext(Pane p)
    {
        var next = _queue.TakeNext();
        if (next is null && _repeatAllOn && _queue.Played.Count > 0)
        {
            _queue.RefillFromPlayed(_shuffleOn);
            next = _queue.TakeNext();
        }
        if (next is null) { p.StopAndBlank(); return; }
        p.Play(next);
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11)
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        else if (e.Key == Key.Escape && _panelOpen)
            ClosePanel();
    }

    private void OnPlaylistDroppedOnPane(Pane targetPane, VideoItem item)
    {
        // Boot current video to bottom of queue
        if (targetPane.Current is not null)
            _queue.Reinsert(targetPane.Current, int.MaxValue);
        // Pull the dropped item out of queue and play it
        _queue.Remove(item);
        _queue.MarkInFlight(item);
        targetPane.Play(item);
    }

    private void OnPaneDroppedOnPane(Pane targetPane, int otherIndex)
    {
        if (otherIndex < 0 || otherIndex >= _panes.Count) return;
        var other = _panes[otherIndex];
        var a = targetPane.Current;
        var b = other.Current;
        if (a is null && b is null) return;

        if (a is not null) other.Play(a);
        else other.StopAndBlank();

        if (b is not null) targetPane.Play(b);
        else targetPane.StopAndBlank();
    }

    private void OnPaneDroppedOnPlaylist(int paneIndex, int dropPosition)
    {
        if (paneIndex < 0 || paneIndex >= _panes.Count) return;
        var pane = _panes[paneIndex];
        if (pane.Current is null) return;
        _queue.Reinsert(pane.Current, dropPosition);
        pane.StopAndBlank();
        // pane will be filled from queue by next idle tick? We do it explicitly:
        var next = _queue.TakeNext();
        if (next is not null) pane.Play(next);
    }
}
