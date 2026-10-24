using FluentAssertions;
using PanesLocal.Services;
using Xunit;

namespace PanesLocal.Tests;

public class FolderScannerTests : IDisposable
{
    private readonly string _root;

    public FolderScannerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "panes-local-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private void Touch(string relPath)
    {
        var full = Path.Combine(_root, relPath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, new byte[] { 0 });
    }

    [Fact]
    public async Task Scan_IncludesVideoExtensionsCaseInsensitive()
    {
        Touch("a.mp4"); Touch("b.MKV"); Touch("c.WebM"); Touch("d.mov");

        var result = await new FolderScanner().ScanAsync(_root, progress: null, ct: default);

        result.Select(v => Path.GetFileName(v.FilePath)).Should().BeEquivalentTo(
            new[] { "a.mp4", "b.MKV", "c.WebM", "d.mov" });
    }

    [Fact]
    public async Task Scan_ExcludesNonVideoExtensions()
    {
        Touch("a.mp4"); Touch("ignore.txt"); Touch("img.jpg"); Touch("audio.mp3");

        var result = await new FolderScanner().ScanAsync(_root, progress: null, ct: default);

        result.Should().HaveCount(1);
        result[0].FilePath.Should().EndWith("a.mp4");
    }

    [Fact]
    public async Task Scan_Recurses()
    {
        Touch("a.mp4"); Touch("sub/b.mp4"); Touch("sub/deep/c.mp4");

        var result = await new FolderScanner().ScanAsync(_root, progress: null, ct: default);

        result.Should().HaveCount(3);
    }

    [Fact]
    public async Task Scan_SkipsHiddenFiles()
    {
        Touch("a.mp4"); Touch(".hidden.mp4");
        File.SetAttributes(Path.Combine(_root, ".hidden.mp4"), FileAttributes.Hidden);

        var result = await new FolderScanner().ScanAsync(_root, progress: null, ct: default);

        result.Should().HaveCount(1);
        result[0].FilePath.Should().EndWith("a.mp4");
    }

    [Fact]
    public async Task Scan_ReportsProgress()
    {
        Touch("a.mp4"); Touch("b.mp4"); Touch("c.mp4");
        var counts = new List<int>();

        await new FolderScanner().ScanAsync(_root, progress: new Progress<int>(n => counts.Add(n)), ct: default);

        counts.Should().NotBeEmpty();
        counts.Last().Should().Be(3);
    }

    [Fact]
    public async Task Scan_HonorsCancellationBeforeStart()
    {
        for (int i = 0; i < 100; i++) Touch($"v{i}.mp4");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = () => new FolderScanner().ScanAsync(_root, progress: null, ct: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Scan_HonorsCancellationDuringScan()
    {
        for (int i = 0; i < 2000; i++) Touch($"v{i}.mp4");
        using var cts = new CancellationTokenSource();

        var scan = new FolderScanner().ScanAsync(_root, progress: null, ct: cts.Token);
        cts.CancelAfter(1);

        Func<Task> act = () => scan;

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
