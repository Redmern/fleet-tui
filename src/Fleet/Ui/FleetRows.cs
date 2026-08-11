using Fleet.Ui.Models;
using Terminal.Gui.Views;

namespace Fleet.Ui;

public static class FleetRows
{
    public static void Fill(
        ListView list, IReadOnlyList<FleetRow> rows, int item = 0, bool spaced = false)
    {
        var source = new FleetRowSource(rows, spaced);

        list.Source = source;

        Select(list, item);
    }

    public static int Selected(ListView list) =>
        list.Source is FleetRowSource source
            ? source.ItemAt(list.SelectedItem ?? 0)
            : list.SelectedItem ?? 0;

    public static void Select(ListView list, int item)
    {
        if (list.Source is not FleetRowSource source)
        {
            list.SelectedItem = item;
            return;
        }

        list.SelectedItem = source.Items == 0
            ? 0
            : source.IndexOf(Math.Clamp(item, 0, source.Items - 1));
    }

    public static void KeepOffSpacers(ListView list)
    {
        var last = 0;

        list.ValueChanged += (_, _) =>
        {
            if (list.Source is not FleetRowSource source || source.Stride == 1)
            {
                return;
            }

            var index = list.SelectedItem ?? 0;

            if (source.Holds(index))
            {
                last = index;
                return;
            }

            var forwards = index >= last;
            var wanted = forwards ? index + 1 : index - 1;

            if (!source.Holds(wanted))
            {
                wanted = forwards ? index - 1 : index + 1;
            }

            if (source.Holds(wanted))
            {
                last = wanted;
                list.SelectedItem = wanted;
            }
        };
    }
}
