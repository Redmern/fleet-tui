using System.Text.Json.Serialization;

namespace Fleet.Platform.Mux.Embedded.Protocol;

public enum MessageType : byte
{
    Hello = 1,
    Welcome = 2,
    Error = 3,
    Key = 10,
    Text = 11,
    Resize = 12,
    Command = 13,
    Badge = 14,
    Mouse = 15,
    Frame = 20,
    HostEffect = 21,
    Bye = 22,
    Request = 30,
    Response = 31,
}

public static class ClientRoles
{
    public const string Attach = "attach";
    public const string Control = "control";
}

public sealed class Hello
{
    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("role")]
    public string Role { get; set; } = ClientRoles.Control;

    [JsonPropertyName("os")]
    public string Os { get; set; } = string.Empty;

    [JsonPropertyName("cols")]
    public int Cols { get; set; }

    [JsonPropertyName("rows")]
    public int Rows { get; set; }

    [JsonPropertyName("workspace")]
    public string? Workspace { get; set; }

    [JsonPropertyName("client")]
    public string? Client { get; set; }
}

public sealed class Welcome
{
    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("client")]
    public string Client { get; set; } = string.Empty;
}

public sealed class ErrorMessage
{
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}

public sealed class Win32Key
{
    [JsonPropertyName("vk")]
    public int Vk { get; set; }

    [JsonPropertyName("sc")]
    public int Sc { get; set; }

    [JsonPropertyName("uc")]
    public int Uc { get; set; }

    [JsonPropertyName("kd")]
    public bool Down { get; set; }

    [JsonPropertyName("cs")]
    public uint State { get; set; }

    [JsonPropertyName("rc")]
    public int Repeat { get; set; }
}

public sealed class KeyMessage
{
    [JsonPropertyName("key")]
    public int Key { get; set; }

    [JsonPropertyName("mods")]
    public int Mods { get; set; }

    [JsonPropertyName("consumed")]
    public int Consumed { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("action")]
    public int Action { get; set; } = 1;

    [JsonPropertyName("unshifted")]
    public uint Unshifted { get; set; }

    [JsonPropertyName("repeat")]
    public int Repeat { get; set; } = 1;

    [JsonPropertyName("win32")]
    public Win32Key? Win32 { get; set; }
}

public static class MouseButtons
{
    public const int None = 0;
    public const int Left = 1;
    public const int Right = 2;
    public const int Middle = 3;
    public const int WheelUp = 4;
    public const int WheelDown = 5;
    public const int WheelLeft = 6;
    public const int WheelRight = 7;
}

public static class MouseActions
{
    public const int Press = 0;
    public const int Release = 1;
    public const int Motion = 2;
}

public sealed class MouseMessage
{
    [JsonPropertyName("x")]
    public int X { get; set; }

    [JsonPropertyName("y")]
    public int Y { get; set; }

    [JsonPropertyName("button")]
    public int Button { get; set; }

    [JsonPropertyName("action")]
    public int Action { get; set; }

    [JsonPropertyName("mods")]
    public int Mods { get; set; }

    [JsonPropertyName("held")]
    public bool Held { get; set; }
}

public sealed class TextMessage
{
    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("bytes")]
    public string? Bytes { get; set; }

    [JsonPropertyName("paste")]
    public bool Paste { get; set; }
}

public sealed class ResizeMessage
{
    [JsonPropertyName("cols")]
    public int Cols { get; set; }

    [JsonPropertyName("rows")]
    public int Rows { get; set; }
}

public sealed class CommandMessage
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("arg")]
    public string? Arg { get; set; }

    [JsonPropertyName("key")]
    public KeyMessage? Key { get; set; }

    [JsonPropertyName("bytes")]
    public string? Bytes { get; set; }
}

public sealed class BadgeMessage
{
    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("keys")]
    public List<WhichKeyEntry>? Keys { get; set; }
}

public sealed class WhichKeyEntry
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;
}

public static class HostEffects
{
    public const string Title = "title";
    public const string Clipboard = "clipboard";
}

public sealed class HostEffect
{
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    public string? Value { get; set; }
}

public sealed class ControlRequest
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("op")]
    public string Op { get; set; } = string.Empty;

    [JsonPropertyName("client")]
    public string? Client { get; set; }

    [JsonPropertyName("pane")]
    public string? Pane { get; set; }

    [JsonPropertyName("caller")]
    public string? Caller { get; set; }

    [JsonPropertyName("workspace")]
    public string? Workspace { get; set; }

    [JsonPropertyName("session")]
    public string? Session { get; set; }

    [JsonPropertyName("window")]
    public string? Window { get; set; }

    [JsonPropertyName("newWindow")]
    public bool NewWindow { get; set; }

    [JsonPropertyName("cwd")]
    public string? Cwd { get; set; }

    [JsonPropertyName("args")]
    public List<string>? Args { get; set; }

    [JsonPropertyName("env")]
    public Dictionary<string, string>? Env { get; set; }

    [JsonPropertyName("direction")]
    public string? Direction { get; set; }

    [JsonPropertyName("percent")]
    public int Percent { get; set; }

    [JsonPropertyName("movePane")]
    public string? MovePane { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("cols")]
    public int Cols { get; set; }

    [JsonPropertyName("rows")]
    public int Rows { get; set; }
}

public sealed class PaneDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("window")]
    public string Window { get; set; } = string.Empty;

    [JsonPropertyName("tab")]
    public string Tab { get; set; } = string.Empty;

    [JsonPropertyName("session")]
    public string Session { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("cwd")]
    public string Cwd { get; set; } = string.Empty;

    [JsonPropertyName("active")]
    public bool Active { get; set; }

    [JsonPropertyName("paneTitle")]
    public string PaneTitle { get; set; } = string.Empty;
}

public sealed class WorkspaceDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("shownHere")]
    public bool ShownHere { get; set; }
}

public sealed class ControlResponse
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("pane")]
    public string? Pane { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("ms")]
    public double Ms { get; set; }

    [JsonPropertyName("cols")]
    public int Cols { get; set; }

    [JsonPropertyName("rows")]
    public int Rows { get; set; }

    [JsonPropertyName("status")]
    public DaemonStatusDto? Status { get; set; }

    [JsonPropertyName("panes")]
    public List<PaneDto>? Panes { get; set; }

    [JsonPropertyName("workspaces")]
    public List<WorkspaceDto>? Workspaces { get; set; }
}

public sealed class DaemonStatusDto
{
    [JsonPropertyName("pid")]
    public int Pid { get; set; }

    [JsonPropertyName("executable")]
    public string Executable { get; set; } = string.Empty;

    [JsonPropertyName("workspaces")]
    public int Workspaces { get; set; }

    [JsonPropertyName("panes")]
    public int Panes { get; set; }

    [JsonPropertyName("warmMenus")]
    public int WarmMenus { get; set; }

    [JsonPropertyName("clients")]
    public int Clients { get; set; }

    [JsonPropertyName("sessionFile")]
    public string? SessionFile { get; set; }
}
