namespace Fleet.Shared.Iso;

public static class IsoProjection
{
    public const string Working = "working";

    public const string Waiting = "waiting for input";

    public const string Idle = "idle";

    public const string Done = "done";

    public const string Failed = "failed";

    public const string Stopped = "stopped";

    public const string Stalled = "stalled";

    public const string BranchTrouble = "branch trouble";

    public const string Ack = "ok";

    public const string Refused = "refused in ISO mode";

    public static string State(string status, bool open) =>
        status.Trim().ToLowerInvariant() switch
        {
            "working" => Working,
            "waiting" or "blocked" or "permission" => Waiting,
            "idle" or "stalled" => Idle,
            "done" => Done,
            "failed" => Failed,
            "" => open ? Idle : Stopped,
            _ => open ? Working : Stopped,
        };

    public static string Notice(string kind) =>
        kind.Trim().ToLowerInvariant() switch
        {
            "done" => Done,
            "failed" => Failed,
            "permission" or "needsinput" => Waiting,
            "stalled" => Stalled,
            "branchtrouble" => BranchTrouble,
            _ => Working,
        };

    public static string Line(string code, string state) => $"{code}: {state}";
}
