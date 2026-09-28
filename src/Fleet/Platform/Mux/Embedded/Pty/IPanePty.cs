namespace Fleet.Platform.Mux.Embedded.Pty;

public interface IPanePty : IDisposable
{
    event Action<byte[], int>? Output;

    event Action<int>? Exited;

    void Start(
        string program,
        IReadOnlyList<string> args,
        int cols,
        int rows,
        string cwd,
        IReadOnlyDictionary<string, string> env);

    void Write(ReadOnlySpan<byte> data);

    void Resize(int cols, int rows);

    static IPanePty Create() =>
        OperatingSystem.IsWindows() ? new WindowsPanePty() : new UnixPanePty();
}
