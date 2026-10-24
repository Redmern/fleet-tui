namespace Fleet.Ports.Editors;

public interface IFileEditor : IEditorLauncher
{
    string? EditAndWait(string file, Action closed);
}
