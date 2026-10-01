using System.Text;
using Fleet.Platform.Mux.Embedded.Input;
using Fleet.Platform.Mux.Embedded.Native;

namespace Fleet.Tests.Platform.Mux.Embedded;

public class FloatModeTests
{
    private static FloatMode Entered()
    {
        var mode = new FloatMode();
        mode.Enter();
        return mode;
    }

    [Theory]
    [InlineData(Key.H, Mods.None, "float-move", "left")]
    [InlineData(Key.ArrowDown, Mods.None, "float-move", "down")]
    [InlineData(Key.K, Mods.None, "float-move", "up")]
    [InlineData(Key.L, Mods.Shift, "float-size", "right")]
    [InlineData(Key.ArrowLeft, Mods.Shift, "float-size", "left")]
    public void Keys_move_and_shifted_keys_resize(Key key, Mods mods, string name, string arg)
    {
        var mode = Entered();

        var step = mode.OnKey(key, mods);

        Assert.Equal((name, arg), (step!.Name, step.Arg));
        Assert.True(mode.Active);
    }

    [Theory]
    [InlineData(Key.Escape)]
    [InlineData(Key.Enter)]
    [InlineData(Key.Q)]
    [InlineData(Key.G)]
    public void Escape_enter_q_and_g_leave_the_mode(Key key)
    {
        var mode = Entered();

        Assert.Null(mode.OnKey(key, Mods.None));
        Assert.False(mode.Active);
    }

    [Fact]
    public void Other_keys_are_swallowed_and_keep_the_mode()
    {
        var mode = Entered();

        Assert.Null(mode.OnKey(Key.X, Mods.None));
        Assert.True(mode.Active);
    }

    [Theory]
    [InlineData("h", 1, "float-move", "left")]
    [InlineData("J", 1, "float-size", "down")]
    [InlineData("\e[C", 3, "float-move", "right")]
    [InlineData("\e[1;2A", 6, "float-size", "up")]
    public void Bytes_are_read_the_same_way_including_arrow_sequences(string typed, int consumed, string name, string arg)
    {
        var mode = Entered();

        var used = mode.OnBytes(Encoding.ASCII.GetBytes(typed + "xyz"), out var step);

        Assert.Equal(consumed, used);
        Assert.Equal((name, arg), (step!.Name, step.Arg));
    }

    [Fact]
    public void A_lone_escape_byte_leaves_the_mode()
    {
        var mode = Entered();

        Assert.Equal(1, mode.OnBytes("\e"u8, out var step));
        Assert.Null(step);
        Assert.False(mode.Active);
    }
}
