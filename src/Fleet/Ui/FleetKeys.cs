using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace Fleet.Ui;

public static class FleetKeys
{
    public static readonly Key Down = Key.J;
    public static readonly Key Up = Key.K;
    public static readonly Key Open = Key.L;
    public static readonly Key First = Key.G;
    public static readonly Key Last = Key.G.WithShift;
    public static readonly Key PageDown = Key.D.WithCtrl;
    public static readonly Key PageUp = Key.U.WithCtrl;
    public static readonly Key Quit = Key.Q;
    public static readonly Key Cancel = Key.Esc;
    public static readonly Key New = Key.N;
    public static readonly Key Add = Key.A;
    public static readonly Key Refresh = Key.R;

    public static void ApplyMotions(View view)
    {
        Bind(view, Down, Command.Down);
        Bind(view, Up, Command.Up);
        Bind(view, First, Command.Start);
        Bind(view, Last, Command.End);
        Bind(view, PageDown, Command.PageDown);
        Bind(view, PageUp, Command.PageUp);
    }

    public static void ApplyOpen(View view) => Bind(view, Open, Command.Accept);

    private static void Bind(View view, Key key, params Command[] commands)
        => view.KeyBindings.ReplaceCommands(key, commands);
}
