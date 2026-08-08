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
        view.KeyBindings.Add(Down, Command.Down);
        view.KeyBindings.Add(Up, Command.Up);
        view.KeyBindings.Add(First, Command.Start);
        view.KeyBindings.Add(Last, Command.End);
        view.KeyBindings.Add(PageDown, Command.PageDown);
        view.KeyBindings.Add(PageUp, Command.PageUp);
    }

    public static void ApplyOpen(View view) => view.KeyBindings.Add(Open, Command.Accept);
}
