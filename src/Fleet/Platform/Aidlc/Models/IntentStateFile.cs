namespace Fleet.Platform.Aidlc.Models;

public sealed class IntentStateFile
{
    public int Version { get; set; } = 1;

    public string Slug { get; set; } = string.Empty;

    public string Profile { get; set; } = string.Empty;

    public string Autonomy { get; set; } = string.Empty;

    public List<StageFile> Stages { get; set; } = [];

    public List<UnitFile> Units { get; set; } = [];

    public string Created { get; set; } = string.Empty;

    public string Updated { get; set; } = string.Empty;
}

public sealed class StageFile
{
    public string Stage { get; set; } = string.Empty;

    public string State { get; set; } = string.Empty;

    public bool HumanGate { get; set; }

    public string Reason { get; set; } = string.Empty;
}

public sealed class UnitFile
{
    public string Id { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Repository { get; set; } = string.Empty;

    public string Branch { get; set; } = string.Empty;

    public List<string> DependsOn { get; set; } = [];

    public List<string> Acceptance { get; set; } = [];

    public List<string> Owns { get; set; } = [];

    public string Verify { get; set; } = string.Empty;

    public bool Skeleton { get; set; }

    public string State { get; set; } = string.Empty;
}
