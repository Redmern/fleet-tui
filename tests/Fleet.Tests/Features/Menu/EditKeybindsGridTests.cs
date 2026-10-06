using Fleet.Features.Menu.EditKeybinds;
using Fleet.Shared.Keymap;
using Fleet.Shared.Keymap.Enums;
using Fleet.Ui.Constants;

namespace Fleet.Tests.Features.Menu;

public class EditKeybindsGridTests
{
    private static IReadOnlyList<EditKeybindsBox> RealBoxes() => EditKeybindsGrid.Boxes(EditKeybindsRows.Build());

    private static EditKeybindsBox Box(string title, int cells) =>
        new(title, [.. Enumerable.Range(0, cells).Select(i => new EditKeybindsRow($"{title} {i}", FleetAction.Refresh, false))]);

    private static string Key(EditKeybindsRow row) => "k";

    [Fact]
    public void The_prefix_gets_its_own_box_first_and_every_group_follows_in_order()
    {
        var boxes = RealBoxes();

        Assert.Equal(EditKeybindsGrid.PrefixTitle, boxes[0].Title);
        Assert.Single(boxes[0].Cells);
        Assert.Null(boxes[0].Cells[0].Action);

        Assert.Equal(KeymapGroups.All.Select(g => g.Label), boxes.Skip(1).Select(b => b.Title));
        Assert.Equal(
            KeymapGroups.All.Select(g => g.Actions.ToList()),
            boxes.Skip(1).Select(b => b.Cells.Select(c => c.Action!.Value).ToList()));
    }

    [Fact]
    public void Header_rows_never_become_cells()
    {
        Assert.All(RealBoxes().SelectMany(b => b.Cells), c => Assert.False(c.IsHeader));
    }

    [Theory]
    [InlineData(200, 3)]
    [InlineData(64, 3)]
    [InlineData(63, 2)]
    [InlineData(42, 2)]
    [InlineData(41, 1)]
    [InlineData(10, 1)]
    public void The_column_count_follows_the_width(int width, int expected)
    {
        Assert.Equal(expected, EditKeybindsGrid.Columns(width, 20, 8));
    }

    [Fact]
    public void Never_more_columns_than_boxes()
    {
        Assert.Equal(2, EditKeybindsGrid.Columns(500, 20, 2));
    }

    [Fact]
    public void Placement_keeps_the_order_and_balances_the_columns()
    {
        Assert.Equal([0, 1, 2], EditKeybindsGrid.Place([3, 3, 3], 3));
        Assert.Equal([0, 1, 1, 1, 1, 1], EditKeybindsGrid.Place([5, 1, 1, 1, 1, 1], 2));
        Assert.Equal([0, 0, 0], EditKeybindsGrid.Place([3, 3, 3], 1));
    }

    [Fact]
    public void Placement_never_uses_more_columns_than_asked()
    {
        var heights = RealBoxes().Select(EditKeybindsGrid.Height).ToList();

        for (var columns = 1; columns <= 3; columns++)
        {
            var placed = EditKeybindsGrid.Place(heights, columns);

            Assert.True(placed[^1] < columns);
            Assert.Equal(placed.OrderBy(c => c), placed);
        }
    }

    [Fact]
    public void Boxes_stack_inside_a_column()
    {
        var boxes = new[] { Box("a", 2), Box("b", 3), Box("c", 4) };

        var layout = EditKeybindsGrid.Layout(boxes, 1, 15);

        Assert.Equal(1, layout.Columns);
        Assert.Equal([0, 4, 9], layout.TopOf);
        Assert.Equal(15, layout.ColumnWidth);
    }

    [Fact]
    public void Down_and_up_walk_the_cells_and_wrap_from_box_to_box()
    {
        var boxes = new[] { Box("a", 2), Box("b", 3) };

        Assert.Equal(new EditKeybindsSpot(0, 1), EditKeybindsGrid.Down(boxes, new(0, 0)));
        Assert.Equal(new EditKeybindsSpot(1, 0), EditKeybindsGrid.Down(boxes, new(0, 1)));
        Assert.Equal(new EditKeybindsSpot(0, 0), EditKeybindsGrid.Down(boxes, new(1, 2)));

        Assert.Equal(new EditKeybindsSpot(0, 1), EditKeybindsGrid.Up(boxes, new(1, 0)));
        Assert.Equal(new EditKeybindsSpot(1, 2), EditKeybindsGrid.Up(boxes, new(0, 0)));
        Assert.Equal(new EditKeybindsSpot(1, 2), EditKeybindsGrid.Last(boxes));
    }

    [Fact]
    public void Left_and_right_move_to_the_box_beside_on_the_same_line_and_wrap()
    {
        var boxes = new[] { Box("a", 4), Box("b", 4), Box("c", 1), Box("d", 2) };
        var layout = EditKeybindsGrid.Layout(boxes, 1, 200);

        Assert.Equal(3, layout.Columns);
        Assert.Equal([0, 1, 2, 2], layout.ColumnOf);

        Assert.Equal(new EditKeybindsSpot(1, 2), EditKeybindsGrid.Right(boxes, layout, new(0, 2)));
        Assert.Equal(new EditKeybindsSpot(3, 0), EditKeybindsGrid.Right(boxes, layout, new(1, 3)));
        Assert.Equal(new EditKeybindsSpot(0, 0), EditKeybindsGrid.Right(boxes, layout, new(2, 0)));
        Assert.Equal(new EditKeybindsSpot(2, 0), EditKeybindsGrid.Left(boxes, layout, new(0, 0)));
        Assert.Equal(new EditKeybindsSpot(0, 3), EditKeybindsGrid.Left(boxes, layout, new(1, 3)));
    }

    [Fact]
    public void In_one_column_left_and_right_step_between_boxes()
    {
        var boxes = new[] { Box("a", 4), Box("b", 2) };
        var layout = EditKeybindsGrid.Layout(boxes, 1, 20);

        Assert.Equal(1, layout.Columns);
        Assert.Equal(new EditKeybindsSpot(1, 1), EditKeybindsGrid.Right(boxes, layout, new(0, 3)));
        Assert.Equal(new EditKeybindsSpot(1, 0), EditKeybindsGrid.Left(boxes, layout, new(0, 0)));
    }

    [Fact]
    public void Every_drawn_line_is_as_wide_as_the_grid()
    {
        var boxes = RealBoxes();

        foreach (var width in new[] { 40, 90, 160 })
        {
            var layout = EditKeybindsGrid.Layout(boxes, EditKeybindsGrid.KeyWidth(boxes, Key), width);
            var picture = EditKeybindsGrid.Draw(boxes, layout, Key, new(0, 0));
            var expected = layout.Columns * layout.ColumnWidth + (layout.Columns - 1) * EditKeybindsGrid.Gap;

            Assert.All(picture.Lines, line => Assert.Equal(expected, line.Sum(s => s.Text.Length)));
        }
    }

    [Fact]
    public void A_box_has_a_titled_top_edge_and_keys_sit_left_in_the_key_tone()
    {
        var boxes = new[] { Box("dash", 2) };
        var layout = EditKeybindsGrid.Layout(boxes, 3, 30);

        var picture = EditKeybindsGrid.Draw(boxes, layout, r => r.Label.EndsWith('0') ? "Spc" : "r", new(0, 0));
        var text = picture.Lines.Select(l => string.Concat(l.Select(s => s.Text))).ToList();

        Assert.StartsWith("╭ dash ─", text[0]);
        Assert.StartsWith("│ Spc  dash 0", text[1]);
        Assert.StartsWith("│ r    dash 1", text[2]);
        Assert.StartsWith("╰─", text[3]);
        Assert.Contains(picture.Lines[1], s => s.Tone == FleetTones.Key && s.Text == "Spc");
        Assert.Contains(picture.Lines[2], s => s.Tone == FleetTones.Key && s.Text == "r  ");
    }

    [Fact]
    public void The_highlight_covers_the_inside_of_the_selected_cell()
    {
        var boxes = new[] { Box("a", 2), Box("b", 2) };
        var layout = EditKeybindsGrid.Layout(boxes, 1, 60);

        var picture = EditKeybindsGrid.Draw(boxes, layout, Key, new(1, 1));
        var left = layout.Left(1);

        Assert.Equal((2, left + 1, left + layout.ColumnWidth - 1), picture.Highlight);
    }

    [Fact]
    public void Long_labels_are_cut_to_fit_a_narrow_box()
    {
        var boxes = new[] { new EditKeybindsBox("t", [new EditKeybindsRow(new string('x', 50), FleetAction.Refresh, false)]) };
        var layout = EditKeybindsGrid.Layout(boxes, 1, 20);

        var line = string.Concat(EditKeybindsGrid.Draw(boxes, layout, Key, new(0, 0)).Lines[1].Select(s => s.Text));

        Assert.Equal(20, line.Length);
        Assert.Contains("…", line);
        Assert.EndsWith("│", line);
    }

    [Fact]
    public void The_key_column_is_as_wide_as_the_longest_key()
    {
        var boxes = new[] { Box("a", 2) };

        Assert.Equal(1, EditKeybindsGrid.KeyWidth(boxes, _ => string.Empty));
        Assert.Equal(9, EditKeybindsGrid.KeyWidth(boxes, r => r.Label.EndsWith('1') ? "ctrl+bksp" : "q"));
    }
}
