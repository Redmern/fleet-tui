namespace Fleet.Platform.Mux.Embedded.Input;

public readonly record struct PasteChunk(byte[] Bytes, bool Pasted);
