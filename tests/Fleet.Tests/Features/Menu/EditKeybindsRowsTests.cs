using Fleet.Features.Menu.EditKeybinds;
using Fleet.Shared.Keymap;
using Fleet.Shared.Keymap.Enums;

namespace Fleet.Tests.Features.Menu;

public class EditKeybindsRowsTests
{
    [Fact]
    public void The_prefix_row_comes_first_and_is_not_a_header()
    {
        var first = EditKeybindsRows.Build()[0];

        Assert.Equal("Prefix", first.Label);
        Assert.Null(first.Action);
        Assert.False(first.IsHeader);
    }

    [Fact]
    public void Each_group_gets_a_header_row_followed_by_its_actions_in_order()
    {
        var rows = EditKeybindsRows.Build();
        var index = 1;

        foreach (var group in KeymapGroups.All)
        {
            var header = rows[index++];
            Assert.True(header.IsHeader);
            Assert.Equal(group.Label, header.Label);
            Assert.Null(header.Action);

            foreach (var action in group.Actions)
            {
                var row = rows[index++];
                Assert.False(row.IsHeader);
                Assert.Equal(action, row.Action);
                Assert.Equal(KeymapDefaults.Describe(action), row.Label);
            }
        }

        Assert.Equal(rows.Count, index);
    }

    [Fact]
    public void The_settings_group_carries_view_logs()
    {
        var rows = EditKeybindsRows.Build();
        var settings = KeymapGroups.All.Single(g => g.Label == "fleet menu > settings");

        Assert.Contains(FleetAction.ViewLogs, settings.Actions);
        Assert.Contains(rows, r => r.Action == FleetAction.ViewLogs && !r.IsHeader);
    }
}
