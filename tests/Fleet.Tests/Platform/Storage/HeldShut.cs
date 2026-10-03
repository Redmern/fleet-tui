namespace Fleet.Tests.Platform.Storage;

// Holds a file open with FileShare.None and lets go of it after a fixed delay, from a dedicated
// thread rather than the thread pool, so the release does not wait for a pool thread on a busy
// runner while the code under test blocks synchronously in its retry window.
public sealed class HeldShut : IDisposable
{
    private readonly Thread _release;

    private HeldShut(string file, TimeSpan holdFor)
    {
        var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None);
        _release = new Thread(() =>
        {
            Thread.Sleep(holdFor);
            stream.Dispose();
        })
        {
            IsBackground = true,
            Name = "HeldShut release",
        };
        _release.Start();
    }

    public static HeldShut For(string file, TimeSpan holdFor) => new(file, holdFor);

    public void Dispose() => _release.Join();
}
