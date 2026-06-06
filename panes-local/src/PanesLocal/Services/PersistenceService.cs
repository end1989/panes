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
    private CancellationTokenSource? _debounceCts;
    private AppState? _pending;

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

    public async Task SaveImmediateAsync(AppState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var json = JsonSerializer.Serialize(state, _jsonOpts);
        var tmp = _path + ".tmp";
        await File.WriteAllTextAsync(tmp, json);
        File.Move(tmp, _path, overwrite: true);
    }

    public void SaveDebounced(AppState state)
    {
        _pending = state;
        _debounceCts?.Cancel();
        _debounceCts = new CancellationTokenSource();
        var ct = _debounceCts.Token;

        _ = Task.Run(async () =>
        {
            try { await Task.Delay(_debounceMs, ct); }
            catch (TaskCanceledException) { return; }

            var snapshot = _pending;
            if (snapshot is null) return;
            try { await SaveImmediateAsync(snapshot); } catch { /* swallow; UI flow */ }
        });
    }
}
