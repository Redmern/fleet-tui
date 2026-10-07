using Fleet.Cli.Composition;
using Fleet.Ports.Keymap;
using Fleet.Shared.Keymap.Models;

namespace Fleet.Tests.Cli;

public sealed class ApplyingKeymapStoreTests
{
    private sealed class RecordingStore(List<string> calls) : IKeymapStore
    {
        public KeymapConfig Load() => throw new NotSupportedException();

        public void Save(KeymapConfig config) => calls.Add("save");
    }

    [Fact]
    public void Saving_applies_the_keybinds_after_the_keymap_is_on_disk()
    {
        var calls = new List<string>();
        var store = new ApplyingKeymapStore(new RecordingStore(calls), () => calls.Add("apply"));

        store.Save(null!);

        Assert.Equal(["save", "apply"], calls);
    }
}
