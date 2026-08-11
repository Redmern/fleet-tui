using Terminal.Gui.Input;
using Terminal.Gui.Views;

namespace Fleet.Ui;

public sealed class FleetList : ListView
{
    public FleetList()
    {
        AddCommand(Command.Down, () => Step(1));
        AddCommand(Command.Up, () => Step(-1));
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

        if (Source is FleetRowSource rows && !rows.Holds(index))
        {
            index = Wrap(index + delta, count);
        }

        SelectedItem = index;

        return true;
    }
}
