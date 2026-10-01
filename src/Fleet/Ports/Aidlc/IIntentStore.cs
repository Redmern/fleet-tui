using Fleet.Shared.Aidlc.Models;

namespace Fleet.Ports.Aidlc;

public interface IIntentStore
{
    IntentState? Load(string folder);

    void Save(string folder, IntentState state);

    void Append(string folder, AuditEntry entry);

    IReadOnlyList<AuditEntry> Audit(string folder);
}
