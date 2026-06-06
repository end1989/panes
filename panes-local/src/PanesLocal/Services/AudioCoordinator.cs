namespace PanesLocal.Services;

public class AudioCoordinator
{
    public int? UnmutedPaneIndex { get; private set; }
    public event Action? Changed;

    public bool IsUnmuted(int paneIndex) => UnmutedPaneIndex == paneIndex;

    public void UnmutePane(int paneIndex)
    {
        if (UnmutedPaneIndex == paneIndex) return;
        UnmutedPaneIndex = paneIndex;
        Changed?.Invoke();
    }

    public void MutePane(int paneIndex)
    {
        if (UnmutedPaneIndex != paneIndex) return;
        UnmutedPaneIndex = null;
        Changed?.Invoke();
    }

    public void MuteAll()
    {
        if (UnmutedPaneIndex is null) return;
        UnmutedPaneIndex = null;
        Changed?.Invoke();
    }
}
