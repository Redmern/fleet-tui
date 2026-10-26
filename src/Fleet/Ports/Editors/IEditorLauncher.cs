namespace Fleet.Ports.Editors;

public interface IEditorLauncher
{
    bool Available { get; }

    string? LaunchDetached(string folder);
}
