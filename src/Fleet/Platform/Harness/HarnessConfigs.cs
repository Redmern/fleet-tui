using Fleet.Ports.Harness;

namespace Fleet.Platform.Harness;

public sealed class HarnessConfigs(IReadOnlyDictionary<string, IHarnessConfig> byHarness, IHarnessConfig fallback)
    : IHarnessConfigs
{
    public IHarnessConfig For(string harness) =>
        byHarness.TryGetValue(harness.Trim(), out var config) ? config : fallback;
}
