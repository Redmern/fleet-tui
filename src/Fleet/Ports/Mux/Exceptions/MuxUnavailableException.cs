namespace Fleet.Ports.Mux.Exceptions;

public sealed class MuxUnavailableException(string message, Exception? inner = null)
    : Exception(message, inner);
