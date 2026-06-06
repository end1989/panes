using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using LibVLCSharp.Shared;
using PanesLocal.Models;
using PanesLocal.Services;

namespace PanesLocal.Views;

public partial class Pane : UserControl, IDisposable
{
    public int PaneIndex { get; set; }
    public VideoItem? Current { get; private set; }
    public double CurrentPositionSeconds => _player?.Time / 1000.0 ?? 0;

    public event Action<Pane>? RequestNext;
    public event Action<Pane>? RequestUnmute;
    public event Action<Pane>? RequestMute;
    public event Action<Pane, VideoItem>? VideoStarted;
    public event Action<Pane, VideoItem>? VideoFinished;

    private MediaPlayer? _player;
    private SharedQueue? _queue;
    private AudioCoordinator? _audio;

    public Pane()
    {
        InitializeComponent();
        MouseEnter += (_, _) => FadeOverlay(1);
        MouseLeave += (_, _) => FadeOverlay(0);

        PreviewMouseLeftButtonDown += OnPaneMouseDown;
        PreviewMouseMove += OnPaneMouseMove;
        Drop += OnPaneDrop;
        DragEnter += (_, _) => DragHighlight.Visibility = Visibility.Visible;
        DragLeave += (_, _) => DragHighlight.Visibility = Visibility.Collapsed;
        DragOver += (s, e) =>
        {
            e.Effects = e.Data.GetDataPresent(SidePanel.VideoItemFormat)
                       || e.Data.GetDataPresent(SidePanel.PaneIndexFormat)
                ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        };
    }

    private Point _paneDragStart;

    private void OnPaneMouseDown(object sender, MouseButtonEventArgs e)
    {
        _paneDragStart = e.GetPosition(null);
    }

    private void OnPaneMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        if (Current is null) return;
        var p = e.GetPosition(null);
        if (Math.Abs(p.X - _paneDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(p.Y - _paneDragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        if (HoverOverlay.IsMouseOver) return;       // don't drag when clicking the overlay buttons
        var data = new DataObject(SidePanel.PaneIndexFormat, PaneIndex);
        DragDrop.DoDragDrop(this, data, DragDropEffects.Move);
    }

    public event Action<Pane, VideoItem>? PlaylistItemDroppedOnMe;     // raised on playlist→pane drop
    public event Action<Pane, int>? PaneDroppedOnMe;                   // raised on pane→pane drop (other pane index)

    private void OnPaneDrop(object sender, DragEventArgs e)
    {
        DragHighlight.Visibility = Visibility.Collapsed;

        if (e.Data.GetDataPresent(SidePanel.VideoItemFormat))
        {
            var item = (VideoItem)e.Data.GetData(SidePanel.VideoItemFormat)!;
            PlaylistItemDroppedOnMe?.Invoke(this, item);
            return;
        }

        if (e.Data.GetDataPresent(SidePanel.PaneIndexFormat))
        {
            var otherIdx = (int)e.Data.GetData(SidePanel.PaneIndexFormat)!;
            if (otherIdx == PaneIndex) return;
            PaneDroppedOnMe?.Invoke(this, otherIdx);
        }
    }

    public void Attach(SharedQueue queue, AudioCoordinator audio)
    {
        _queue = queue;
        _audio = audio;
        _audio.Changed += SyncMuteToCoordinator;

        _player = new MediaPlayer(App.LibVLC) { Volume = 0 };
        _player.EndReached += OnEndReached;
        _player.EncounteredError += OnError;
        VlcView.MediaPlayer = _player;
    }

    public void Play(VideoItem item, double? seekSeconds = null)
    {
        if (_player is null) return;
        Current = item;
        EmptyHint.Visibility = Visibility.Collapsed;
        VlcView.Visibility = Visibility.Visible;
        using var media = new Media(App.LibVLC, item.FilePath, FromType.FromPath);
        _player.Play(media);
        if (seekSeconds is double s && s > 0)
            _player.Time = (long)(s * 1000);
        VideoStarted?.Invoke(this, item);
    }

    public void StopAndBlank()
    {
        if (_player is null) return;
        if (Current is not null) VideoFinished?.Invoke(this, Current);
        _player.Stop();
        Current = null;
        VlcView.Visibility = Visibility.Collapsed;
        EmptyHint.Visibility = Visibility.Visible;
    }

    private void OnEndReached(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            if (Current is not null) _queue?.OnFinished(Current.FilePath);
            RequestNext?.Invoke(this);
        });
    }

    private void OnError(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            if (Current is not null) _queue?.OnFinished(Current.FilePath);
            RequestNext?.Invoke(this);
        });
    }

    private void SyncMuteToCoordinator()
    {
        if (_player is null || _audio is null) return;
        _player.Volume = _audio.IsUnmuted(PaneIndex) ? 100 : 0;
        MuteBtn.Content = _audio.IsUnmuted(PaneIndex) ? "🔊" : "🔇";
    }

    private void MuteBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_audio is null) return;
        if (_audio.IsUnmuted(PaneIndex)) RequestMute?.Invoke(this);
        else RequestUnmute?.Invoke(this);
    }

    private void SkipBtn_Click(object sender, RoutedEventArgs e)
    {
        if (Current is not null) _queue?.OnFinished(Current.FilePath);
        RequestNext?.Invoke(this);
    }

    private void StopBtn_Click(object sender, RoutedEventArgs e)
    {
        if (Current is not null) _queue?.Reinsert(Current, int.MaxValue);
        Current = null;
        _player?.Stop();
    }

    private void FadeOverlay(double targetOpacity)
    {
        var anim = new DoubleAnimation(targetOpacity, TimeSpan.FromMilliseconds(150));
        HoverOverlay.BeginAnimation(OpacityProperty, anim);
    }

    public void Dispose()
    {
        if (_player is not null)
        {
            _player.EndReached -= OnEndReached;
            _player.EncounteredError -= OnError;
            _player.Stop();
            _player.Dispose();
        }
        if (_audio is not null) _audio.Changed -= SyncMuteToCoordinator;
    }
}
