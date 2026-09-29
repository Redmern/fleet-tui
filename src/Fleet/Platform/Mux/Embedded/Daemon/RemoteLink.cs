using Fleet.Platform.Mux.Embedded.Protocol;
using Fleet.Shared.Constants;

namespace Fleet.Platform.Mux.Embedded.Daemon;

public sealed record RemoteChannel(Stream Stream, TextReader? Errors, IDisposable Owner);

public sealed class RemoteLink(string host, Func<string, RemoteChannel> open, Action<string> log)
{
    public const string Connecting = "connecting";
    public const string Asking = "asking";
    public const string Connected = "connected";
    public const string Failed = "failed";

    public static readonly TimeSpan RefreshEvery = TimeSpan.FromSeconds(2);

    private readonly Lock _gate = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Queue<string> _errors = new();
    private string _state = Connecting;
    private string? _name;
    private string? _error;
    private string? _prompt;
    private string? _answer;
    private IReadOnlyList<string> _projects = [];

    public string Host => host;

    public string Token { get; } = Guid.NewGuid().ToString("N");

    public RemoteDto Snapshot()
    {
        lock (_gate)
        {
            return new RemoteDto
            {
                Host = host,
                Name = _name ?? host,
                State = _state,
                Error = _error,
                Prompt = _state == Asking ? _prompt : null,
                Secret = _prompt is not null && IsSecret(_prompt),
                Projects = [.. _projects],
            };
        }
    }

    public static bool IsSecret(string prompt) => !prompt.Contains("yes/no", StringComparison.OrdinalIgnoreCase);

    public (bool Pending, string? Answer) Ask(string prompt)
    {
        lock (_gate)
        {
            if (_answer is { } answer && _prompt == prompt)
            {
                _answer = null;
                _prompt = null;
                _state = Connecting;
                return (false, answer);
            }

            _prompt = prompt;
            _state = Asking;
            return (true, null);
        }
    }

    public void Answer(string answer)
    {
        lock (_gate)
        {
            _answer = answer;
        }
    }

    public void Stop() => _stop.Cancel();

    public async Task RunAsync(CancellationToken daemon)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(daemon, _stop.Token);
        var ct = linked.Token;
        RemoteChannel? channel = null;

        try
        {
            channel = open(Token);
            if (channel.Errors is { } errors)
            {
                _ = Task.Run(() => CollectErrorsAsync(errors), CancellationToken.None);
            }

            var opened = false;
            using var remote = new EmbeddedDriver(
                new Endpoint(host),
                open: _ =>
                {
                    if (opened)
                    {
                        return Task.FromResult<Stream?>(null);
                    }

                    opened = true;
                    return Task.FromResult<Stream?>(channel.Stream);
                });

            var status = await remote.StatusAsync(ct).ConfigureAwait(false);
            lock (_gate)
            {
                _name = status?.Host is { Length: > 0 } name ? name : host;
            }

            log($"remote {host}: connected to {_name ?? host}");

            while (!ct.IsCancellationRequested)
            {
                var workspaces = await remote.ListWorkspacesAsync(ct).ConfigureAwait(false);
                lock (_gate)
                {
                    _projects = [.. workspaces.Select(w => w.Name).Where(n => !FleetWorkspaces.IsHidden(n)).Order(StringComparer.OrdinalIgnoreCase)];
                    _state = Connected;
                    _error = null;
                }

                await Task.Delay(RefreshEvery, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or ObjectDisposedException
                                      or System.ComponentModel.Win32Exception or Ports.Mux.Exceptions.MuxUnavailableException)
        {
            await Task.Delay(300, CancellationToken.None).ConfigureAwait(false);

            lock (_gate)
            {
                _state = Failed;
                _error = _errors.Count > 0 ? string.Join(" ", _errors) : e.Message;
            }

            log($"remote {host}: {_error}");
        }
        finally
        {
            channel?.Owner.Dispose();
        }
    }

    private async Task CollectErrorsAsync(TextReader errors)
    {
        try
        {
            while (await errors.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                if (line.Trim().Length == 0)
                {
                    continue;
                }

                lock (_gate)
                {
                    _errors.Enqueue(line.Trim());
                    while (_errors.Count > 3)
                    {
                        _errors.Dequeue();
                    }
                }
            }
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or ObjectDisposedException)
        {
        }
    }
}
