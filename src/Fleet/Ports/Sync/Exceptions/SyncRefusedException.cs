namespace Fleet.Ports.Sync.Exceptions;

public sealed class SyncRefusedException(string reason) : Exception(reason);
