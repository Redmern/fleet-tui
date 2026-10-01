namespace Fleet.Platform.Mux.Embedded.Pty;

public sealed class RemotePty : IPanePty
{
    public const int DefaultCols = 120;

    public const int DefaultRows = 40;

    private int _exited;

    public event Action<byte[], int>? Output;

    public event Action<int>? Exited;

    public event Action<int, int>? Resized;

    public (int Cols, int Rows) Size { get; private set; } = (DefaultCols, DefaultRows);

    public void Start(
        string program,
        IReadOnlyList<string> args,
        int cols,
        int rows,
        string cwd,
        IReadOnlyDictionary<string, string> env) => Resize(cols, rows);

    public void Write(ReadOnlySpan<byte> data)
    {
    }

    public void Resize(int cols, int rows)
    {
        if ((cols, rows) == Size || cols < 2 || rows < 2)
        {
            return;
        }

        Size = (cols, rows);
        Resized?.Invoke(cols, rows);
    }

    public void Emit(byte[] bytes) => Output?.Invoke(bytes, bytes.Length);

    public void Exit()
    {
        if (Interlocked.Exchange(ref _exited, 1) == 0)
        {
            Exited?.Invoke(0);
        }
    }

    public void Dispose()
    {
    }
}
