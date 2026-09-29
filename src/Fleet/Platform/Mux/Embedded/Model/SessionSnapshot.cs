using System.Text.Json.Serialization;

namespace Fleet.Platform.Mux.Embedded.Model;

public sealed class SessionSnapshot
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("workspaces")]
    public List<WorkspaceSnapshot> Workspaces { get; set; } = [];
}

public sealed class WorkspaceSnapshot
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("activeTab")]
    public int ActiveTab { get; set; }

    [JsonPropertyName("tabs")]
    public List<TabSnapshot> Tabs { get; set; } = [];

    [JsonPropertyName("floats")]
    public List<FloatSnapshot> Floats { get; set; } = [];

    [JsonPropertyName("floatsShown")]
    public bool FloatsShown { get; set; }

    [JsonPropertyName("floatFocused")]
    public bool FloatFocused { get; set; }
}

public sealed class TabSnapshot
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("root")]
    public LayoutSnapshot Root { get; set; } = new();

    [JsonPropertyName("activePane")]
    public int ActivePane { get; set; }

    [JsonPropertyName("zoomed")]
    public int Zoomed { get; set; } = -1;
}

public sealed class LayoutSnapshot
{
    [JsonPropertyName("pane")]
    public PaneSnapshot? Pane { get; set; }

    [JsonPropertyName("sideBySide")]
    public bool SideBySide { get; set; }

    [JsonPropertyName("ratio")]
    public double Ratio { get; set; }

    [JsonPropertyName("first")]
    public LayoutSnapshot? First { get; set; }

    [JsonPropertyName("second")]
    public LayoutSnapshot? Second { get; set; }
}

public sealed class PaneSnapshot
{
    [JsonPropertyName("cwd")]
    public string Cwd { get; set; } = string.Empty;

    [JsonPropertyName("args")]
    public List<string> Args { get; set; } = [];

    [JsonPropertyName("env")]
    public Dictionary<string, string> Env { get; set; } = [];
}

public sealed class FloatSnapshot
{
    [JsonPropertyName("pane")]
    public PaneSnapshot Pane { get; set; } = new();

    [JsonPropertyName("x")]
    public int X { get; set; }

    [JsonPropertyName("y")]
    public int Y { get; set; }

    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(SessionSnapshot))]
public sealed partial class SessionJsonContext : JsonSerializerContext;
