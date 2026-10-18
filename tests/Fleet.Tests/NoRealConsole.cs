using System.Runtime.CompilerServices;

namespace Fleet.Tests;

// Terminal.Gui's own switch for tests: drivers skip the real console and run in degraded mode. Without it,
// Init("ansi") on a Windows runner opens CONIN$ and its input thread can block in ReadFile until the job
// times out (Linux runners have no /dev/tty, so they never hit it).
internal static class NoRealConsole
{
    [ModuleInitializer]
    internal static void Disable() => Environment.SetEnvironmentVariable("DisableRealDriverIO", "1");
}
