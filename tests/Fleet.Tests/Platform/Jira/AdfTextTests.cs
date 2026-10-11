using System.Text.Json;
using Fleet.Platform.Jira;

namespace Fleet.Tests.Platform.Jira;

public class AdfTextTests
{
    private static string Text(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return AdfText.From(doc.RootElement);
    }

    private static string Node(string type, string attrs, params string[] content) =>
        "{ \"type\": \"" + type + "\", " + (attrs.Length > 0 ? "\"attrs\": " + attrs + ", " : string.Empty)
        + "\"content\": [" + string.Join(',', content) + "] }";

    private static string Doc(params string[] blocks) => Node("doc", string.Empty, blocks);

    private static string Run(string text) => "{ \"type\": \"text\", \"text\": \"" + text + "\" }";

    private static string Para(string text) => Node("paragraph", string.Empty, Run(text));

    private static string Item(params string[] blocks) => Node("listItem", string.Empty, blocks);

    [Fact]
    public void Paragraphs_are_separated_by_a_blank_line() =>
        Assert.Equal("one\n\ntwo", Text(Doc(Para("one"), Para("two"))));

    [Fact]
    public void Text_runs_join_and_a_hard_break_is_a_new_line() =>
        Assert.Equal(
            "bold and plain\nnext",
            Text(Doc(Node(
                "paragraph",
                string.Empty,
                "{ \"type\": \"text\", \"text\": \"bold\", \"marks\": [{ \"type\": \"strong\" }] }",
                Run(" and plain"),
                "{ \"type\": \"hardBreak\" }",
                Run("next")))));

    [Fact]
    public void A_heading_gets_hashes_for_its_level() =>
        Assert.Equal(
            "## Steps\n\ndo it",
            Text(Doc(Node("heading", "{ \"level\": 2 }", Run("Steps")), Para("do it"))));

    [Fact]
    public void Bullet_and_ordered_lists_get_markers()
    {
        var bullets = Node("bulletList", string.Empty, Item(Para("a")), Item(Para("b")));
        var numbers = Node("orderedList", "{ \"order\": 3 }", Item(Para("c")), Item(Para("d")));

        Assert.Equal("- a\n- b\n\n3. c\n4. d", Text(Doc(bullets, numbers)));
    }

    [Fact]
    public void A_nested_list_is_indented()
    {
        var nested = Node("bulletList", string.Empty, Item(Para("outer"), Node("bulletList", string.Empty, Item(Para("inner")))));

        Assert.Equal("- outer\n  - inner", Text(Doc(nested)));
    }

    [Fact]
    public void A_code_block_is_fenced() =>
        Assert.Equal(
            "```\nvar x = 1;\n```",
            Text(Doc(Node("codeBlock", "{ \"language\": \"cs\" }", Run("var x = 1;")))));

    [Fact]
    public void Unknown_nodes_keep_their_text() =>
        Assert.Equal(
            "ping @Bob now\n\ninside a panel",
            Text(Doc(
                Node(
                    "paragraph",
                    string.Empty,
                    Run("ping "),
                    "{ \"type\": \"mention\", \"attrs\": { \"id\": \"1\", \"text\": \"@Bob\" } }",
                    Run(" now")),
                Node("panel", "{ \"panelType\": \"info\" }", Para("inside a panel")))));

    [Fact]
    public void A_missing_or_plain_description_is_handled()
    {
        Assert.Equal(string.Empty, Text("null"));
        Assert.Equal("plain text", Text("\"plain text\""));
        Assert.Equal(string.Empty, Text(Doc()));
    }
}
