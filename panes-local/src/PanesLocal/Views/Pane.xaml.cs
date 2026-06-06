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
