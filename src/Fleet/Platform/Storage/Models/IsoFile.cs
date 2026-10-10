namespace Fleet.Platform.Storage.Models;

public sealed class IsoFile
{
    public int Version { get; set; } = 1;

    public bool On { get; set; }

    public List<string> AttachFrom { get; set; } = [];

    public Dictionary<string, string> Codes { get; set; } = [];
}
