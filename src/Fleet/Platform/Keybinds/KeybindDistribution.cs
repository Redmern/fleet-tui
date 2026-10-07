using Fleet.Platform.Claude;
using Fleet.Platform.Claude.Models;
using Fleet.Ports.Keybinds.Enums;
using Fleet.Platform.Keybinds.Models;
using Fleet.Ports.Keybinds.Models;
using Fleet.Platform.Nvim;
using Fleet.Shared.Keybinds;
using Fleet.Shared.Keybinds.Enums;

namespace Fleet.Platform.Keybinds;

public sealed class KeybindDistribution(KeybindSet set, KeybindTargets targets, KeybindOs? os = null, Action<string>? log = null)
{
    public static readonly IReadOnlyList<KeybindTarget> Rendered = [KeybindTarget.Nvim, KeybindTarget.Claude];

    private readonly KeybindOs _os = os ?? KeybindNames.CurrentOs;

    private readonly Action<string> _log = log ?? (_ => { });

    public IReadOnlyList<KeybindApplied> Apply(IReadOnlyCollection<KeybindTarget> only, bool dryRun)
    {
        var applied = new List<KeybindApplied>();

        if (only.Contains(KeybindTarget.Nvim))
        {
            applied.AddRange(Nvim(dryRun));
        }

        if (only.Contains(KeybindTarget.Claude))
        {
            var wanted = new Lazy<IReadOnlyList<ClaudeKeybinding>>(() => ClaudeKeybindings.Render(set, _os, dryRun ? null : _log));
            applied.AddRange(targets.ClaudeHomes.Select(home => Claude(home, wanted, dryRun)));
        }

        return applied;
    }

    public IReadOnlyList<KeybindApplied> Drift() =>
        [.. Apply(Rendered, dryRun: true).Where(a => a.Outcome is KeybindOutcome.Stale or KeybindOutcome.Failed)];

    private IEnumerable<KeybindApplied> Nvim(bool dryRun)
    {
        var files = new NvimKeybindFiles(targets.NvimDirectory, _os);

        yield return File.Exists(Path.Combine(targets.NvimDirectory, "init.lua"))
            ? Lua(files.GeneratedFile, () => files.GeneratedIsCurrent(set), () => files.WriteGenerated(set), dryRun)
            : new KeybindApplied(
                KeybindTarget.Nvim, files.GeneratedFile, KeybindOutcome.Skipped, "fleet-nvim is not installed; 'fleet setup' installs it");

        yield return Lua(
            targets.UserModule,
            () => files.UserModuleIsCurrent(set, targets.UserModule),
            () => files.WriteUserModule(set, targets.UserModule),
            dryRun);
    }

    private static KeybindApplied Lua(string path, Func<bool> current, Func<bool> write, bool dryRun)
    {
        if (current())
        {
            return new KeybindApplied(KeybindTarget.Nvim, path, KeybindOutcome.Current);
        }

        if (dryRun)
        {
            return new KeybindApplied(KeybindTarget.Nvim, path, KeybindOutcome.Stale);
        }

        return write()
            ? new KeybindApplied(KeybindTarget.Nvim, path, KeybindOutcome.Written)
            : new KeybindApplied(KeybindTarget.Nvim, path, KeybindOutcome.Failed, "could not write it");
    }

    private static KeybindApplied Claude(string home, Lazy<IReadOnlyList<ClaudeKeybinding>> wanted, bool dryRun)
    {
        try
        {
            return Claude(new ClaudeKeybindingsWriter(home), home, wanted.Value, dryRun);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return new KeybindApplied(KeybindTarget.Claude, home, KeybindOutcome.Failed, e.Message);
        }
    }

    private static KeybindApplied Claude(ClaudeKeybindingsWriter writer, string home, IReadOnlyList<ClaudeKeybinding> wanted, bool dryRun)
    {
        if (!Directory.Exists(home))
        {
            return new KeybindApplied(
                KeybindTarget.Claude, writer.KeybindingsFile, KeybindOutcome.Skipped, "no Claude config folder there");
        }

        if (dryRun)
        {
            var current = writer.IsCurrent(wanted);

            return !current.Succeeded
                ? new KeybindApplied(KeybindTarget.Claude, writer.KeybindingsFile, KeybindOutcome.Failed, current.Error!)
                : new KeybindApplied(
                    KeybindTarget.Claude,
                    writer.KeybindingsFile,
                    current.Value ? KeybindOutcome.Current : KeybindOutcome.Stale);
        }

        var written = writer.Write(wanted);

        if (!written.Succeeded)
        {
            return new KeybindApplied(KeybindTarget.Claude, writer.KeybindingsFile, KeybindOutcome.Failed, written.Error!);
        }

        var kept = written.Value.Conflicts.Count == 0
            ? string.Empty
            : "kept your own binding for " + string.Join(", ", written.Value.Conflicts.Select(c => $"{c.Key} in {c.Context}"));

        return new KeybindApplied(
            KeybindTarget.Claude,
            writer.KeybindingsFile,
            written.Value.Changed ? KeybindOutcome.Written : KeybindOutcome.Current,
            kept);
    }
}
