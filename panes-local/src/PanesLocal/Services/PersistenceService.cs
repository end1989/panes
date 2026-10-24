using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using PanesLocal.Models;

namespace PanesLocal.Services;

public class PersistenceService
{
    private readonly string _path;
    private readonly int _debounceMs;
    private readonly JsonSerializerOptions _jsonOpts;
    private readonly object _writeLock = new();
    private CancellationTokenSource? _debounceCts;
    private long _latestVersion;
    private long _writtenVersion;

    public bool LastLoadWasFallback { get; private set; }

    public PersistenceService(string path, int debounceMs = 500)
    {
        _path = path;
        _debounceMs = debounceMs;
        _jsonOpts = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };
    }

    public AppState Load()
    {
        LastLoadWasFallback = false;
        if (!File.Exists(_path)) return new AppState();
        try
        {
            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<AppState>(json, _jsonOpts) ?? new AppState();
        }
        catch
        {
            LastLoadWasFallback = true;
            return new AppState();
        }
    }

    // Synchronous so the final save on close is on disk before the process exits.
    // Also supersedes any debounced save that hasn't been written yet.
    public void SaveImmediate(AppState state)
    {
        var version = Interlocked.Increment(ref _latestVersion);
        _debounceCts?.Cancel();
        Write(state, version);
    }

    public void SaveDebounced(AppState state)
    {
        var version = Interlocked.Increment(ref _latestVersion);
        _debounceCts?.Cancel();
        _debounceCts = new CancellationTokenSource();
        var ct = _debounceCts.Token;

        _ = Task.Run(async () =>
        {
            try { await Task.Delay(_debounceMs, ct); }
            catch (TaskCanceledException) { return; }

            try { Write(state, version); } catch { /* swallow; UI flow */ }
        });
    }

    // Wipes the saved state. Saves that haven't reached disk yet are dropped so
    // they can't bring the old state back.
    public void Delete()
    {
        lock (_writeLock)
        {
            _debounceCts?.Cancel();
            _writtenVersion = Interlocked.Increment(ref _latestVersion);
            if (File.Exists(_path)) File.Delete(_path);
        }
    }

    private void Write(AppState state, long version)
    {
        lock (_writeLock)
        {
            // A newer snapshot already reached disk; writing this one would roll it back.
            if (version <= _writtenVersion) return;

            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var json = JsonSerializer.Serialize(state, _jsonOpts);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, _path, overwrite: true);
            _writtenVersion = version;
        }
    }
}
