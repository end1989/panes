using System.IO;
using PanesLocal.Models;

namespace PanesLocal.Services;

public class FolderScanner
{
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".avi", ".mov", ".webm", ".m4v",
        ".wmv", ".flv", ".ts", ".mpg", ".mpeg", ".ogv"
    };

    public async Task<List<VideoItem>> ScanAsync(
        string rootPath,
        IProgress<int>? progress,
        CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            var results = new List<VideoItem>();
            ScanDir(rootPath, results, progress, ct);
            return results;
        }, ct);
    }

    private static void ScanDir(
        string dir,
        List<VideoItem> results,
        IProgress<int>? progress,
        CancellationToken ct)
    {
        IEnumerable<string> entries;
        try
        {
            entries = Directory.EnumerateFileSystemEntries(dir);
        }
        catch (UnauthorizedAccessException) { return; }
        catch (DirectoryNotFoundException)  { return; }

        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();

            FileAttributes attrs;
            try { attrs = File.GetAttributes(entry); }
            catch { continue; }

            if ((attrs & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
            if ((attrs & FileAttributes.ReparsePoint) != 0) continue;          // no symlinks

            if ((attrs & FileAttributes.Directory) != 0)
            {
                ScanDir(entry, results, progress, ct);
                continue;
            }

            var ext = Path.GetExtension(entry);
            if (!VideoExtensions.Contains(ext)) continue;

            long size = 0;
            try { size = new FileInfo(entry).Length; } catch { }

            results.Add(new VideoItem(entry, Path.GetFileName(entry), size));
            progress?.Report(results.Count);
        }
    }
}
