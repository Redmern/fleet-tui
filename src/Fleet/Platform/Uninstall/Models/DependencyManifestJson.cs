namespace Fleet.Platform.Uninstall.Models;

public sealed class DependencyManifestJson
{
    public int? Version { get; set; }

    public List<DependencyEntryJson?>? Deps { get; set; }
}
