using System.IO;
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
    private readonly PersistenceService _persistence = new(App.StateFilePath);
    private AppState _state = new();

    public MainWindow()
    {
        InitializeComponent();
        KeyDown += OnKeyDown;
        MouseMove += OnMouseMove;
        Panel.MouseEnter += (_, _) => _mouseLeftPanelAt = DateTime.MaxValue;
        Panel.MouseLeave += (_, _) => _mouseLeftPanelAt = DateTime.Now;
        Panel.PreviewMouseLeftButtonDown += (_, _) => _panelAnchored = true;

        _idleTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _idleTimer.Tick += (_, _) => { if ((DateTime.Now - _lastMouseMove).TotalSeconds > 3) Cursor = Cursors.None; };
        _idleTimer.Start();

        _panelTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _panelTimer.Tick += (_, _) => UpdatePanelState();
        _panelTimer.Start();

        _state = _persistence.Load();
        _cols = _state.Grid.Cols;
        _rows = _state.Grid.Rows;
        _zoom = _state.Zoom;
        _shuffleOn = _state.Transport.Shuffle;
        _repeatAllOn = _state.Transport.RepeatAll;
        _dock = _state.Panel.Dock;
        Width  = _state.Window.Width;
        Height = _state.Window.Height;
        Left   = _state.Window.X;
        Top    = _state.Window.Y;
        if (_state.Window.Maximized) WindowState = WindowState.Maximized;

        // Restore queue: rescan folders + filter saved paths
        if (_state.Folders.Count > 0)
        {
            _ = RestoreSessionAsync();
        }

        Panel.Attach(_queue, _audio);
        Panel.SetGrid(_cols, _rows);
        Panel.SetZoom(_zoom);
        Panel.SetShuffle(_shuffleOn);
        Panel.SetRepeatAll(_repeatAllOn);

        Panel.GridSizeChanged += (c, r) => { _cols = c; _rows = r; BuildGrid(); SaveState(); };
        Panel.ZoomChanged += z => { _zoom = z; ApplyZoom(); SaveState(); };
        Panel.FolderChosen += async p => { _state.Folders.Clear(); _state.Folders.Add(p); await LoadFolderAsync(p, true); SaveState(); };
        Panel.FolderAdded  += async p => { if (!_state.Folders.Contains(p)) _state.Folders.Add(p); await LoadFolderAsync(p, false); SaveState(); };
        Panel.QueueCleared += () => { _queue.Clear(); foreach (var pn in _panes) pn.StopAndBlank(); SaveState(); };
        Panel.ShuffleToggled += on => { _shuffleOn = on; SaveState(); };
        Panel.RepeatAllToggled += on => { _repeatAllOn = on; SaveState(); };
        Panel.ReshuffleClicked += () => { _queue.Reshuffle(); SaveState(); };
        Panel.PaneDroppedOnPlaylist += OnPaneDroppedOnPlaylist;
        Panel.ResetRequested += () =>
        {
            if (File.Exists(App.StateFilePath)) File.Delete(App.StateFilePath);
            System.Diagnostics.Process.Start(System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName!);
            Application.Current.Shutdown();
        };

        Panel.PaneSkipRequested += idx =>
        {
            if (idx < 0 || idx >= _panes.Count) return;
            var pane = _panes[idx];
            if (pane.Current is not null) _queue.OnFinished(pane.Current.FilePath);
            OnPaneRequestsNext(pane);
        };
        Panel.PaneMuteRequested += idx =>
        {
            if (idx < 0 || idx >= _panes.Count) return;
            if (_audio.IsUnmuted(idx)) _audio.MutePane(idx);
            else _audio.UnmutePane(idx);
        };
        Panel.PaneStopRequested += idx =>
        {
            if (idx < 0 || idx >= _panes.Count) return;
            var pane = _panes[idx];
            if (pane.Current is not null) _queue.Reinsert(pane.Current, int.MaxValue);
            pane.StopAndBlank();
        };

        _queue.Changed += SaveState;
        _queue.Changed += RefreshPaneSlots;
        _audio.Changed += SaveState;
        _audio.Changed += RefreshPaneSlots;

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
            ApplyDock(ResolveDockFromPosition(p));
            SaveState();
        };

        Closing += OnClosing;
        SizeChanged += (_, _) => SaveState();
        LocationChanged += (_, _) => SaveState();

        ApplyDock(_dock);
        BuildGrid();
        ClosePanelInstant();

        // Always open the panel on launch so the user can see the controls.
        // They can close it with Escape, M, or the close chevron.
        Loaded += (_, _) => OpenPanel();

        if (_persistence.LastLoadWasFallback)
            MessageBox.Show("Could not restore last session — starting fresh.", "Panes Local",
                MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async Task RestoreSessionAsync()
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in _state.Folders)
        {
            try
            {
                var items = await _scanner.ScanAsync(folder, progress: null, CancellationToken.None);
                foreach (var i in items) existing.Add(i.FilePath);
            }
            catch { /* folder missing → skip */ }
        }
        // Items the saved queue still finds on disk, in order
        foreach (var path in _state.Queue.Where(existing.Contains))
            _queue.Items.Add(new VideoItem(path, System.IO.Path.GetFileName(path), 0));
        // Played list — keep filtered
        foreach (var path in _state.Played.Where(existing.Contains))
            _queue.Played[path] = new VideoItem(path, System.IO.Path.GetFileName(path), 0);
        // Newly discovered files: append
        var known = new HashSet<string>(_queue.Items.Select(i => i.FilePath));
        foreach (var p in _queue.InFlight) known.Add(p);
        foreach (var p in _queue.Played.Keys) known.Add(p);
        foreach (var path in existing.Where(p => !known.Contains(p)))
            _queue.Items.Add(new VideoItem(path, System.IO.Path.GetFileName(path), 0));

        if (_shuffleOn) _queue.Shuffle();

        // Restore per-pane current video at saved position
        foreach (var ps in _state.Panes)
        {
            if (ps.FilePath is null) continue;
            if (!existing.Contains(ps.FilePath)) continue;
            if (ps.Index < 0 || ps.Index >= _panes.Count) continue;
            var v = new VideoItem(ps.FilePath, System.IO.Path.GetFileName(ps.FilePath), 0);
            _queue.Remove(v);
            _queue.MarkInFlight(v);
            _panes[ps.Index].Play(v, ps.PositionSeconds);
        }

        if (_state.Audio.UnmutedPaneIndex is int u) _audio.UnmutePane(u);
        FillIdlePanesFromQueue();
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        SaveStateSync();
    }

    private void SaveState()
    {
        SyncStateFromUi();
        _persistence.SaveDebounced(_state);
    }

    private void SaveStateSync()
    {
        SyncStateFromUi();
        _ = _persistence.SaveImmediateAsync(_state);
    }

    private void SyncStateFromUi()
    {
        _state.Window = new AppState.WindowState
        {
            X = Left, Y = Top, Width = Width, Height = Height,
            Maximized = WindowState == WindowState.Maximized,
            Fullscreen = false
        };
        _state.Grid = new AppState.GridState { Cols = _cols, Rows = _rows };
        _state.Zoom = _zoom;
        _state.Panel = new AppState.PanelState { Dock = _dock, Size = PanelWidth };
        _state.Transport = new AppState.TransportState { Shuffle = _shuffleOn, RepeatAll = _repeatAllOn };
        _state.Audio = new AppState.AudioState { UnmutedPaneIndex = _audio.UnmutedPaneIndex };
        _state.Queue = _queue.Items.Select(i => i.FilePath).ToList();
        _state.Played = _queue.Played.Keys.ToList();
        _state.Panes = _panes.Select(p => new PaneState(p.PaneIndex, p.Current?.FilePath, p.CurrentPositionSeconds)).ToList();
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
        // Auto-close-on-mouse-leave was too aggressive — it killed the panel
        // mid-interaction. Panel now stays open until the user closes it
        // explicitly (Escape, M, click the close chevron).
    }

    private void OpenPanel()
    {
        _panelOpen = true;
        OpenChevron.Visibility = Visibility.Collapsed;
        ApplyPaneGridInset(open: true);
        Focus();
        AnimatePanel(0);
    }

    private void ClosePanel()
    {
        _panelOpen = false;
        _panelAnchored = false;
        _mouseLeftPanelAt = DateTime.MaxValue;
        OpenChevron.Visibility = Visibility.Visible;
        ApplyPaneGridInset(open: false);
        Focus();
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
        ApplyPaneGridInset(open: false);
    }

    // Shrink the video grid to leave airspace for the panel.
    // libVLC's VideoView uses an HwndHost (native Win32 child window) which
    // always overdraws WPF content, so we can't just float the panel on top
    // of videos — we have to physically vacate the panel's space.
    private void ApplyPaneGridInset(bool open)
    {
        if (!open) { PaneGrid.Margin = new Thickness(0); return; }
        PaneGrid.Margin = _dock switch
        {
            DockSide.Right  => new Thickness(0, 0, PanelWidth, 0),
            DockSide.Left   => new Thickness(PanelWidth, 0, 0, 0),
            DockSide.Top    => new Thickness(0, PanelWidth, 0, 0),
            DockSide.Bottom => new Thickness(0, 0, 0, PanelWidth),
            _ => new Thickness(0)
        };
    }

    private void AnimatePanel(double target)
    {
        // Snap instead of animate. The panel slide-in animation took ~200ms
        // while PaneGrid.Margin changed instantly — that left a black gap
        // during the slide. Instant snap = no transition glitch (the
        // "videos go weird, double M to fix" symptom).
        var prop = (_dock is DockSide.Left or DockSide.Right)
            ? TranslateTransform.XProperty
            : TranslateTransform.YProperty;
        PanelTransform.BeginAnimation(prop, null);
        if (prop == TranslateTransform.XProperty) PanelTransform.X = target;
        else PanelTransform.Y = target;
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
        RefreshPaneSlots();
        FillIdlePanesFromQueue();
    }

    private void ApplyZoom()
    {
        // WPF RenderTransform doesn't affect HwndHost (native Win32 child window
        // used by LibVLCSharp's VideoView), so a ScaleTransform here makes the
        // pane chrome and the video go out of sync and one pane can appear to
        // detach visually. Disabled until we wire libVLC's native crop instead.
    }

    private void RefreshPaneSlots()
    {
        if (Panel is null) return;
        var info = _panes
            .Select(p => (p.PaneIndex, p.Current?.FilePath, _audio.IsUnmuted(p.PaneIndex)))
            .ToList();
        Panel.RefreshSlots(info);
    }

    private async void FillIdlePanesFromQueue()
    {
        foreach (var p in _panes)
        {
            if (p.Current is not null) continue;
            var next = _queue.TakeNext();
            if (next is null) break;
            p.Play(next);
            // Stagger Plays so libVLC/HwndHost can initialize each player
            // before the next one starts — avoids concurrent-init deadlocks.
            await Task.Delay(150);
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
        // Ignore when a text input has focus
        if (FocusManager.GetFocusedElement(this) is TextBox or PasswordBox) return;

        switch (e.Key)
        {
            case Key.F11:
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                break;
            case Key.Escape:
                if (_panelOpen) ClosePanel();
                break;
            case Key.M:
                if (_panelOpen) ClosePanel(); else OpenPanel();
                e.Handled = true;
                break;
            case Key.Space:
                if (_audio.UnmutedPaneIndex is null) _audio.UnmutePane(0);
                else _audio.MuteAll();
                e.Handled = true;
                break;
            case Key.S:
                _shuffleOn = !_shuffleOn;
                Panel.SetShuffle(_shuffleOn);
                SaveState();
                break;
            case Key.R:
                _queue.Reshuffle();
                SaveState();
                break;
        }
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
