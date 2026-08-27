using Terminal.Gui.Input;
using Terminal.Gui.Views;

namespace Fleet.Ui;

public sealed class FleetList : ListView
{
    public FleetList()
    {
        AddCommand(Command.Down, () => Step(1));
        AddCommand(Command.Up, () => Step(-1));
        AddCommand(Command.Start, () => Jump(0, 1));
        AddCommand(Command.End, () => Jump((Source?.Count ?? 1) - 1, -1));
    }

    public static int Wrap(int index, int count) =>
        count <= 0 ? 0 : ((index % count) + count) % count;

    private bool? Step(int delta)
    {
        var count = Source?.Count ?? 0;

        if (count == 0)
        {
            return true;
        }

        var index = Wrap((SelectedItem ?? 0) + delta, count);

        if (Source is FleetRowSource rows)
        {
            for (var hops = 0; hops < count && !rows.Holds(index); hops++)
            {
                index = Wrap(index + delta, count);
            }
        }

        SelectedItem = index;

        return true;
    }

    private bool? Jump(int index, int delta)
    {
        var count = Source?.Count ?? 0;

        if (count == 0)
        {
            return true;
        }

        index = Math.Clamp(index, 0, count - 1);

        if (Source is FleetRowSource rows)
        {
            for (var hops = 0; hops < count && !rows.Holds(index); hops++)
            {
                index = Wrap(index + delta, count);
            }
        }

        SelectedItem = index;

        return true;
    }
}
