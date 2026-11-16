using System.Threading.Channels;

namespace Fleet.Tests.Platform.Mcp.Support;

// A reader a test feeds line by line, so it decides what the server has read before the next line arrives.
// End stands for the client closing its side.
internal sealed class FedReader : TextReader
{
    private readonly Channel<string> _lines = Channel.CreateUnbounded<string>();

    public void Feed(string line) => _lines.Writer.TryWrite(line);

    public void End() => _lines.Writer.TryComplete();

    public override async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        while (await _lines.Reader.WaitToReadAsync(cancellationToken))
        {
            if (_lines.Reader.TryRead(out var line))
            {
                return line;
            }
        }

        return null;
    }
}
