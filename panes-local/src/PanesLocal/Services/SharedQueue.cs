using System.Collections.ObjectModel;
using System.IO;
using PanesLocal.Models;

namespace PanesLocal.Services;

public class SharedQueue
{
    public ObservableCollection<VideoItem> Items { get; } = new();
    public HashSet<string> InFlight { get; } = new();
    public Dictionary<string, VideoItem> Played { get; } = new();

    private readonly Dictionary<string, VideoItem> _inFlightItems = new();

    public event Action? Changed;

    private void NotifyChanged() => Changed?.Invoke();

    public void AddAll(IEnumerable<VideoItem> incoming)
    {
        foreach (var item in incoming)
        {
            if (KnownPath(item.FilePath)) continue;
            Items.Add(item);
        }
        NotifyChanged();
    }

    public VideoItem? TakeNext()
    {
        if (Items.Count == 0) return null;
        var first = Items[0];
        Items.RemoveAt(0);
        InFlight.Add(first.FilePath);
        _inFlightItems[first.FilePath] = first;
        NotifyChanged();
        return first;
    }

    public void OnFinished(string path)
    {
        if (InFlight.Remove(path) && _inFlightItems.Remove(path, out var item))
            Played[path] = item;
        NotifyChanged();
    }

    public void Reinsert(VideoItem item, int position)
    {
        InFlight.Remove(item.FilePath);
        _inFlightItems.Remove(item.FilePath);
        Played.Remove(item.FilePath);
        var existing = Items.FirstOrDefault(i => i.FilePath == item.FilePath);
        if (existing is not null) Items.Remove(existing);
        var idx = Math.Clamp(position, 0, Items.Count);
        Items.Insert(idx, item);
        NotifyChanged();
    }

    public void Remove(VideoItem item)
    {
        var existing = Items.FirstOrDefault(i => i.FilePath == item.FilePath);
        if (existing is not null) Items.Remove(existing);
        InFlight.Remove(item.FilePath);
        _inFlightItems.Remove(item.FilePath);
        Played.Remove(item.FilePath);
        NotifyChanged();
    }

    public void MoveToTop(VideoItem item)
    {
        if (!Items.Remove(item)) return;
        Items.Insert(0, item);
        NotifyChanged();
    }

    public void MoveUp(VideoItem item)
    {
        var idx = Items.IndexOf(item);
        if (idx <= 0) return;
        Items.Move(idx, idx - 1);
        NotifyChanged();
    }

    public void MoveDown(VideoItem item)
    {
        var idx = Items.IndexOf(item);
        if (idx < 0 || idx >= Items.Count - 1) return;
        Items.Move(idx, idx + 1);
        NotifyChanged();
    }

    public void Move(int from, int to)
    {
        if (from < 0 || from >= Items.Count || to < 0 || to >= Items.Count || from == to) return;
        Items.Move(from, to);
        NotifyChanged();
    }

    public void Shuffle()
    {
        var rng = Random.Shared;
        var arr = Items.ToArray();
        for (int i = arr.Length - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (arr[i], arr[j]) = (arr[j], arr[i]);
        }
        Items.Clear();
        foreach (var v in arr) Items.Add(v);
        NotifyChanged();
    }

    public void Reshuffle() => Shuffle();

    public void RefillFromPlayed(bool shuffle)
    {
        var refill = Played.Values.ToList();
        Played.Clear();
        foreach (var v in refill) Items.Add(v);
        if (shuffle) Shuffle();
        NotifyChanged();
    }

    public void MarkInFlight(VideoItem item)
    {
        InFlight.Add(item.FilePath);
        _inFlightItems[item.FilePath] = item;
        NotifyChanged();
    }

    public void Clear()
    {
        Items.Clear();
        InFlight.Clear();
        _inFlightItems.Clear();
        Played.Clear();
        NotifyChanged();
    }

    private bool KnownPath(string path)
        => InFlight.Contains(path)
        || Played.ContainsKey(path)
        || Items.Any(i => i.FilePath == path);
}
