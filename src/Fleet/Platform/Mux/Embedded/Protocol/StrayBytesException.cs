namespace Fleet.Platform.Mux.Embedded.Protocol;

public sealed class StrayBytesException(uint length, byte[] header)
    : IOException($"message length {length} is out of range")
{
    public byte[] Header { get; } = header;
}