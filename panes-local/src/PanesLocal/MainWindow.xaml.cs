using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
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
    private DateTime _lastMouseMove = DateTime.Now;
    private int _cols = 2;
    private int _rows = 2;

    public MainWindow()
    {
        InitializeComponent();
        KeyDown += OnKeyDown;
        MouseMove += (_, _) => { _lastMouseMove = DateTime.Now; Cursor = Cursors.Arrow; };
        _idleTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _idleTimer.Tick += (_, _) =>
        {
            if ((DateTime.Now - _lastMouseMove).TotalSeconds > 3)
                Cursor = Cursors.None;
        };
        _idleTimer.Start();

        BuildGrid();
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
                pane.RequestNext  += OnPaneRequestsNext;
                pane.RequestUnmute += p => _audio.UnmutePane(p.PaneIndex);
                pane.RequestMute   += p => _audio.MutePane(p.PaneIndex);
                Grid.SetColumn(pane, c);
                Grid.SetRow(pane, r);
                PaneGrid.Children.Add(pane);
                _panes.Add(pane);
                idx++;
            }

        FillIdlePanesFromQueue();
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
        if (next is null) { p.StopAndBlank(); return; }
        p.Play(next);
    }

    private async void LoadFolderTempBtn_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog();
        if (dlg.ShowDialog() != true) return;
        LoadFolderTempBtn.Visibility = Visibility.Collapsed;

        var items = await _scanner.ScanAsync(dlg.FolderName, progress: null, CancellationToken.None);
        _queue.AddAll(items);
        _queue.Shuffle();
        FillIdlePanesFromQueue();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }
    }
}
