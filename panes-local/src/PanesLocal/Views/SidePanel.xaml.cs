using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using PanesLocal.Models;
using PanesLocal.Services;

namespace PanesLocal.Views;

public partial class SidePanel : UserControl
{
    public SharedQueue? Queue { get; private set; }
    public AudioCoordinator? Audio { get; private set; }

    public event Action<int, int>? GridSizeChanged;
    public event Action<int>? ZoomChanged;
    public event Action<string>? FolderChosen;
    public event Action<string>? FolderAdded;
    public event Action? QueueCleared;
    public event Action<bool>? ShuffleToggled;
    public event Action<bool>? RepeatAllToggled;
    public event Action? ReshuffleClicked;

    private int _cols = 2;
    private int _rows = 2;

    public SidePanel() { InitializeComponent(); }

    public void Attach(SharedQueue queue, AudioCoordinator audio)
    {
        Queue = queue;
        Audio = audio;
        PlaylistView.ItemsSource = queue.Items;
        queue.Changed += UpdateStatus;
        audio.Changed += UpdateStatus;
        UpdateStatus();
    }

    public void SetGrid(int cols, int rows)
    {
        _cols = cols; _rows = rows;
        ColsLabel.Text = cols.ToString();
        RowsLabel.Text = rows.ToString();
    }

    public void SetZoom(int zoom) => ZoomSlider.Value = zoom;
    public void SetShuffle(bool on) => ShuffleBtn.IsChecked = on;
    public void SetRepeatAll(bool on) => RepeatAllBtn.IsChecked = on;
    public void SetFolderName(string name) => FolderName.Text = name;

    private void UpdateStatus()
    {
        var count = (Queue?.Items.Count ?? 0) + (Queue?.InFlight.Count ?? 0) + (Queue?.Played.Count ?? 0);
        var audioState = Audio?.UnmutedPaneIndex is int i ? $"🔊 Pane {i + 1}" : "🔇 All muted";
        StatusLine.Text = $"{count} videos · {audioState}";
    }

    private void ColsPlus_Click(object s, RoutedEventArgs e)  { if (_cols < 6) { _cols++; SetGrid(_cols, _rows); GridSizeChanged?.Invoke(_cols, _rows); } }
    private void ColsMinus_Click(object s, RoutedEventArgs e) { if (_cols > 1) { _cols--; SetGrid(_cols, _rows); GridSizeChanged?.Invoke(_cols, _rows); } }
    private void RowsPlus_Click(object s, RoutedEventArgs e)  { if (_rows < 6) { _rows++; SetGrid(_cols, _rows); GridSizeChanged?.Invoke(_cols, _rows); } }
    private void RowsMinus_Click(object s, RoutedEventArgs e) { if (_rows > 1) { _rows--; SetGrid(_cols, _rows); GridSizeChanged?.Invoke(_cols, _rows); } }

    private void ZoomSlider_ValueChanged(object s, RoutedPropertyChangedEventArgs<double> e)
        => ZoomChanged?.Invoke((int)e.NewValue);

    private void ShuffleBtn_Click(object s, RoutedEventArgs e)   => ShuffleToggled?.Invoke(ShuffleBtn.IsChecked == true);
    private void RepeatAllBtn_Click(object s, RoutedEventArgs e) => RepeatAllToggled?.Invoke(RepeatAllBtn.IsChecked == true);
    private void ReshuffleBtn_Click(object s, RoutedEventArgs e) => ReshuffleClicked?.Invoke();

    private void ChooseFolderBtn_Click(object s, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog();
        if (dlg.ShowDialog() != true) return;
        SetFolderName(System.IO.Path.GetFileName(dlg.FolderName));
        FolderChosen?.Invoke(dlg.FolderName);
    }

    private void AddFolderBtn_Click(object s, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog();
        if (dlg.ShowDialog() != true) return;
        FolderAdded?.Invoke(dlg.FolderName);
    }

    private void ClearQueueBtn_Click(object s, RoutedEventArgs e) => QueueCleared?.Invoke();
}
