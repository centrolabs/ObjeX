namespace ObjeX.Web.Services;

/// <summary>Lets anything in the circuit open the command palette (the sidebar entry), and spells its shortcut for this platform.</summary>
public sealed class CommandPaletteState
{
    public string Shortcut { get; private set; } = "Ctrl K";

    public event Action? OpenRequested;
    public event Action? Changed;

    public void Open() => OpenRequested?.Invoke();

    public void SetPlatform(bool mac)
    {
        var shortcut = mac ? "⌘K" : "Ctrl K";
        if (shortcut == Shortcut) return;
        Shortcut = shortcut;
        Changed?.Invoke();
    }
}
