namespace Fleet.Platform.Forwards;

public interface ITunnel
{
    Task<int> Exited { get; }

    string Errors { get; }

    void Kill();
}
