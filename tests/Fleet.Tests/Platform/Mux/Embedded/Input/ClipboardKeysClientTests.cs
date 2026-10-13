using System.Text;
using System.Threading.Channels;
using Fleet.Platform.Clipboard;
using Fleet.Platform.Mux.Embedded.Client;
using Fleet.Platform.Mux.Embedded.Input;
using Fleet.Platform.Mux.Embedded.Protocol;

namespace Fleet.Tests.Platform.Mux.Embedded.Input;

public class ClipboardKeysClientTests
{
    private const string CtrlShiftC = "\e[27;6;67~";
    private const string CtrlShiftV = "\e[27;6;86~";
    private const string Prefix = "\u0013";

    [Theory]
    [InlineData(CtrlShiftV)]
    [InlineData("\e[118;6u")]
    public async Task Ctrl_shift_v_pastes_the_local_clipboard_into_the_pane_as_a_paste(string typed)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var messages = await Typed(typed);

        var text = Assert.Single(Texts(messages));
        Assert.Equal("from the clipboard", text.Text);
        Assert.True(text.Paste);
        Assert.Empty(Commands(messages));
    }

    [Theory]
    [InlineData("\u0003")]
    [InlineData("\u0016")]
    public async Task Ctrl_c_and_ctrl_v_reach_the_pane_unchanged(string typed)
    {
        var messages = await Typed(typed);

        var text = Assert.Single(Texts(messages));
        Assert.Equal(Encoding.ASCII.GetBytes(typed), Convert.FromBase64String(text.Bytes!));
        Assert.False(text.Paste);
        Assert.Empty(Commands(messages));
    }

    [Theory]
    [InlineData(CtrlShiftC)]
    [InlineData("\e[99;6u")]
    public async Task Ctrl_shift_c_asks_the_daemon_to_copy_the_current_selection(string typed)
    {
        var messages = await Typed(typed);

        var command = Assert.Single(Commands(messages));
        Assert.Equal(("copy", "selection"), (command.Name, command.Arg));
        Assert.Empty(Texts(messages));
    }

    [Fact]
    public async Task Ctrl_shift_c_in_copy_mode_yanks_like_y()
    {
        var messages = await Typed(Prefix + "[", CtrlShiftC);

        Assert.Equal(
            [("copy-mode", null), ("copy", "yank")],
            Commands(messages).Select(c => (c.Name, c.Arg)));
    }

    private static async Task<List<(MessageType Type, byte[] Payload)>> Typed(params string[] reads)
    {
        using var sent = new MemoryStream();
        var wire = new Wire(sent);
        var clipboard = ClipboardText.Mac(1024, run: (_, _, _, _) =>
            Task.FromResult<ClipboardRun?>(new ClipboardRun(0, Encoding.UTF8.GetBytes("from the clipboard"), false)));
        var client = new AttachClient(Stream.Null, null, () => MuxKeys.Defaults, _ => { }, clipboardText: clipboard);
        var channel = Channel.CreateUnbounded<byte[]>();

        var loop = client.StdinAsync(wire, channel.Reader);
        foreach (var read in reads)
        {
            channel.Writer.TryWrite(Encoding.ASCII.GetBytes(read));
        }

        channel.Writer.Complete();
        await loop.WaitAsync(TimeSpan.FromSeconds(10));

        sent.Position = 0;
        var back = new Wire(sent);
        var messages = new List<(MessageType, byte[])>();
        while (await back.ReceiveAsync() is { } message)
        {
            messages.Add(message);
        }

        return messages;
    }

    private static List<TextMessage> Texts(List<(MessageType Type, byte[] Payload)> messages) =>
        messages.Where(m => m.Type == MessageType.Text)
            .Select(m => Wire.Read(m.Payload, WireJsonContext.Default.TextMessage))
            .ToList();

    private static List<CommandMessage> Commands(List<(MessageType Type, byte[] Payload)> messages) =>
        messages.Where(m => m.Type == MessageType.Command)
            .Select(m => Wire.Read(m.Payload, WireJsonContext.Default.CommandMessage))
            .ToList();
}
