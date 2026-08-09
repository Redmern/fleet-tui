namespace Fleet.Ports.Requests;

public interface IWorkspaceRequestStore
{
    void Submit(string workspace);
}
