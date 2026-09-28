using System.Runtime.InteropServices;

namespace Fleet.Platform.Mux.Embedded.Host;

public sealed class HostSignals : IDisposable
{
    private readonly List<PosixSignalRegistration> _registrations = [];
    private readonly EventHandler _exit;

    private HostSignals(Action leave)
    {
        _exit = (_, _) => leave();
        AppDomain.CurrentDomain.ProcessExit += _exit;

        foreach (var signal in new[] { PosixSignal.SIGTERM, PosixSignal.SIGHUP, PosixSignal.SIGQUIT })
        {
            try
            {
                _registrations.Add(PosixSignalRegistration.Create(signal, _ => leave()));
            }
            catch (PlatformNotSupportedException)
            {
            }
        }
    }

    public static HostSignals OnExit(Action leave) => new(leave);

    public void Dispose()
    {
        AppDomain.CurrentDomain.ProcessExit -= _exit;

        foreach (var registration in _registrations)
        {
            registration.Dispose();
        }
    }
}
