using Fleet.Platform.Mux.Embedded.Native;
using Fleet.Platform.Mux.Embedded.Protocol;

namespace Fleet.Platform.Mux.Embedded.Input;

public sealed class ConfirmMode : IStickyMode
{
    private string _command = string.Empty;

    public bool Active { get; private set; }

    public string Badge { get; private set; } = string.Empty;

    public void Ask(string command, string question)
    {
        _command = command;
        Badge = $"{question} y/n";
        Active = true;
    }

    public CommandMessage? OnKey(Key key, Mods mods, string? text = null) =>
        Answer(key == Key.Y && (mods & (Mods.Ctrl | Mods.Alt)) == 0);

    public int OnBytes(ReadOnlySpan<byte> bytes, out CommandMessage? command)
    {
        command = Answer(bytes[0] is (byte)'y' or (byte)'Y');
        return 1;
    }

    private CommandMessage? Answer(bool yes)
    {
        Active = false;
        return yes ? new CommandMessage { Name = _command } : null;
    }
}