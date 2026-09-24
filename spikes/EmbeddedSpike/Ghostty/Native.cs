using System.Runtime.InteropServices;

namespace EmbeddedSpike.Ghostty;

// Hand-written LibraryImport bindings for the subset of libghostty-vt the spike
// uses, transcribed from include/ghostty/vt/*.h at the commit pinned in
// native/pins.env. With <DirectPInvoke Include="ghostty-vt" /> these become
// direct calls into the statically linked archive; nothing is loaded at runtime.
//
// Struct layouts follow the C headers for x86_64 (LP64 / LLP64 agree here:
// every field is a fixed-width type or size_t). Enums are C ints.
internal static unsafe partial class Native
{
    private const string Lib = "ghostty-vt";

    public const int Success = 0;
    public const int OutOfMemory = -1;
    public const int InvalidValue = -2;
    public const int OutOfSpace = -3;
    public const int NoValue = -4;

    [LibraryImport(Lib, EntryPoint = "ghostty_terminal_new")]
    public static partial int TerminalNew(nint allocator, out nint terminal, ushort cols, ushort rows);

    [LibraryImport(Lib, EntryPoint = "ghostty_terminal_free")]
    public static partial void TerminalFree(nint terminal);

    [LibraryImport(Lib, EntryPoint = "ghostty_terminal_resize")]
    public static partial int TerminalResize(nint terminal, ushort cols, ushort rows, uint cellWidthPx, uint cellHeightPx);

    [LibraryImport(Lib, EntryPoint = "ghostty_terminal_set")]
    public static partial int TerminalSet(nint terminal, TerminalOption option, void* value);

    [LibraryImport(Lib, EntryPoint = "ghostty_terminal_vt_write")]
    public static partial void TerminalVtWrite(nint terminal, byte* data, nuint len);

    [LibraryImport(Lib, EntryPoint = "ghostty_terminal_get")]
    public static partial int TerminalGet(nint terminal, TerminalData data, void* value);

    [LibraryImport(Lib, EntryPoint = "ghostty_render_state_new")]
    public static partial int RenderStateNew(nint allocator, out nint state);

    [LibraryImport(Lib, EntryPoint = "ghostty_render_state_free")]
    public static partial void RenderStateFree(nint state);

    [LibraryImport(Lib, EntryPoint = "ghostty_render_state_update")]
    public static partial int RenderStateUpdate(nint state, nint terminal);

    [LibraryImport(Lib, EntryPoint = "ghostty_render_state_get")]
    public static partial int RenderStateGet(nint state, RenderStateData data, void* value);

    [LibraryImport(Lib, EntryPoint = "ghostty_render_state_set")]
    public static partial int RenderStateSet(nint state, RenderStateOption option, void* value);

    [LibraryImport(Lib, EntryPoint = "ghostty_render_state_row_iterator_new")]
    public static partial int RowIteratorNew(nint allocator, out nint iterator);

    [LibraryImport(Lib, EntryPoint = "ghostty_render_state_row_iterator_free")]
    public static partial void RowIteratorFree(nint iterator);

    [LibraryImport(Lib, EntryPoint = "ghostty_render_state_row_iterator_next")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool RowIteratorNext(nint iterator);

    [LibraryImport(Lib, EntryPoint = "ghostty_render_state_row_get")]
    public static partial int RowGet(nint iterator, RowData data, void* value);

    [LibraryImport(Lib, EntryPoint = "ghostty_render_state_row_set")]
    public static partial int RowSet(nint iterator, RowOption option, void* value);

    [LibraryImport(Lib, EntryPoint = "ghostty_render_state_row_cells_new")]
    public static partial int RowCellsNew(nint allocator, out nint cells);

    [LibraryImport(Lib, EntryPoint = "ghostty_render_state_row_cells_free")]
    public static partial void RowCellsFree(nint cells);

    [LibraryImport(Lib, EntryPoint = "ghostty_render_state_row_cells_next")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool RowCellsNext(nint cells);

    [LibraryImport(Lib, EntryPoint = "ghostty_render_state_row_cells_get")]
    public static partial int RowCellsGet(nint cells, CellsData data, void* value);

    [LibraryImport(Lib, EntryPoint = "ghostty_cell_get")]
    public static partial int CellGet(ulong cell, CellData data, void* value);

    [LibraryImport(Lib, EntryPoint = "ghostty_formatter_terminal_new")]
    public static partial int FormatterTerminalNew(nint allocator, out nint formatter, nint terminal, FormatterTerminalOptions options);

    [LibraryImport(Lib, EntryPoint = "ghostty_formatter_format_alloc")]
    public static partial int FormatterFormatAlloc(nint formatter, nint allocator, out byte* ptr, out nuint len);

    [LibraryImport(Lib, EntryPoint = "ghostty_formatter_free")]
    public static partial void FormatterFree(nint formatter);

    [LibraryImport(Lib, EntryPoint = "ghostty_free")]
    public static partial void Free(nint allocator, byte* ptr, nuint len);

    [LibraryImport(Lib, EntryPoint = "ghostty_key_encoder_new")]
    public static partial int KeyEncoderNew(nint allocator, out nint encoder);

    [LibraryImport(Lib, EntryPoint = "ghostty_key_encoder_free")]
    public static partial void KeyEncoderFree(nint encoder);

    [LibraryImport(Lib, EntryPoint = "ghostty_key_encoder_setopt_from_terminal")]
    public static partial void KeyEncoderSetoptFromTerminal(nint encoder, nint terminal);

    [LibraryImport(Lib, EntryPoint = "ghostty_key_encoder_encode")]
    public static partial int KeyEncoderEncode(nint encoder, nint keyEvent, byte* outBuf, nuint outBufSize, out nuint outLen);

    [LibraryImport(Lib, EntryPoint = "ghostty_key_event_new")]
    public static partial int KeyEventNew(nint allocator, out nint keyEvent);

    [LibraryImport(Lib, EntryPoint = "ghostty_key_event_free")]
    public static partial void KeyEventFree(nint keyEvent);

    [LibraryImport(Lib, EntryPoint = "ghostty_key_event_set_action")]
    public static partial void KeyEventSetAction(nint keyEvent, KeyAction action);

    [LibraryImport(Lib, EntryPoint = "ghostty_key_event_set_key")]
    public static partial void KeyEventSetKey(nint keyEvent, Key key);

    [LibraryImport(Lib, EntryPoint = "ghostty_key_event_set_mods")]
    public static partial void KeyEventSetMods(nint keyEvent, ushort mods);

    [LibraryImport(Lib, EntryPoint = "ghostty_key_event_set_consumed_mods")]
    public static partial void KeyEventSetConsumedMods(nint keyEvent, ushort mods);

    [LibraryImport(Lib, EntryPoint = "ghostty_key_event_set_composing")]
    public static partial void KeyEventSetComposing(nint keyEvent, [MarshalAs(UnmanagedType.U1)] bool composing);

    [LibraryImport(Lib, EntryPoint = "ghostty_key_event_set_utf8")]
    public static partial void KeyEventSetUtf8(nint keyEvent, byte* utf8, nuint len);

    [LibraryImport(Lib, EntryPoint = "ghostty_key_event_set_unshifted_codepoint")]
    public static partial void KeyEventSetUnshiftedCodepoint(nint keyEvent, uint codepoint);
}

internal enum TerminalOption
{
    Userdata = 0,
    WritePty = 1,
    DeviceAttributes = 8,
}

internal enum TerminalData
{
    Cols = 1,
    Rows = 2,
    ActiveScreen = 6,
    KittyKeyboardFlags = 8,
    Title = 12,
}

internal enum RenderStateData
{
    Cols = 1,
    Rows = 2,
    Dirty = 3,
    RowIterator = 4,
    Cursor = 18,
}

internal enum RenderStateOption
{
    Dirty = 0,
}

internal enum RenderStateDirty
{
    False = 0,
    Partial = 1,
    Full = 2,
}

internal enum RowData
{
    Dirty = 1,
    Raw = 2,
    Cells = 3,
}

internal enum RowOption
{
    Dirty = 0,
}

internal enum CellsData
{
    Raw = 1,
    Style = 2,
    BgColor = 5,
    FgColor = 6,
    GraphemesUtf8 = 9,
}

internal enum CellData
{
    Codepoint = 1,
    Wide = 3,
    HasText = 4,
}

internal enum CellWide
{
    Narrow = 0,
    Wide = 1,
    SpacerTail = 2,
    SpacerHead = 3,
}

internal enum StyleColorTag
{
    None = 0,
    Palette = 1,
    Rgb = 2,
}

internal enum KeyAction
{
    Release = 0,
    Press = 1,
    Repeat = 2,
}

[StructLayout(LayoutKind.Sequential)]
internal struct Rgb
{
    public byte R;
    public byte G;
    public byte B;
}

// GhosttyStyleColor: { int tag; union { u8 palette; GhosttyColorRgb rgb; u64 _padding; } }
[StructLayout(LayoutKind.Explicit, Size = 16)]
internal struct StyleColor
{
    [FieldOffset(0)] public StyleColorTag Tag;
    [FieldOffset(8)] public byte Palette;
    [FieldOffset(8)] public Rgb Rgb;
}

// GhosttyStyle, 72 bytes.
[StructLayout(LayoutKind.Explicit, Size = 72)]
internal struct Style
{
    [FieldOffset(0)] public nuint Size;
    [FieldOffset(8)] public StyleColor Fg;
    [FieldOffset(24)] public StyleColor Bg;
    [FieldOffset(40)] public StyleColor Underline;
    [FieldOffset(56)] public byte Bold;
    [FieldOffset(57)] public byte Italic;
    [FieldOffset(58)] public byte Faint;
    [FieldOffset(59)] public byte Blink;
    [FieldOffset(60)] public byte Inverse;
    [FieldOffset(61)] public byte Invisible;
    [FieldOffset(62)] public byte Strikethrough;
    [FieldOffset(63)] public byte Overline;
    [FieldOffset(64)] public int UnderlineKind;
}

// GhosttyRenderStateCursor, 24 bytes.
[StructLayout(LayoutKind.Explicit, Size = 24)]
internal struct RenderCursor
{
    [FieldOffset(0)] public nuint Size;
    [FieldOffset(8)] public byte ViewportHasValue;
    [FieldOffset(10)] public ushort ViewportX;
    [FieldOffset(12)] public ushort ViewportY;
    [FieldOffset(14)] public byte WideTail;
    [FieldOffset(15)] public byte Visible;
    [FieldOffset(16)] public byte Blinking;
    [FieldOffset(17)] public byte PasswordInput;
    [FieldOffset(20)] public int VisualStyle;
}

// GhosttyBuffer: { u8* ptr; size_t cap; size_t len; }
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct Buffer
{
    public byte* Ptr;
    public nuint Cap;
    public nuint Len;
}

// GhosttyString: { const u8* ptr; size_t len; }
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct GhosttyString
{
    public byte* Ptr;
    public nuint Len;
}

// GhosttyFormatterTerminalOptions, 56 bytes, passed by value.
[StructLayout(LayoutKind.Explicit, Size = 56)]
internal struct FormatterTerminalOptions
{
    [FieldOffset(0)] public nuint Size;
    [FieldOffset(8)] public int Emit;
    [FieldOffset(12)] public byte Unwrap;
    [FieldOffset(13)] public byte Trim;
    [FieldOffset(16)] public nuint ExtraSize;
    [FieldOffset(32)] public nuint ScreenExtraSize;
    [FieldOffset(48)] public nint Selection;
}

// GhosttyDeviceAttributes: primary { u16 level; u16 features[64]; size_t n; },
// secondary { u16 type; u16 firmware; u16 rom; }, tertiary { u32 unit }.
[StructLayout(LayoutKind.Explicit, Size = 160)]
internal unsafe struct DeviceAttributes
{
    [FieldOffset(0)] public ushort ConformanceLevel;
    [FieldOffset(2)] public fixed ushort Features[64];
    [FieldOffset(136)] public nuint NumFeatures;
    [FieldOffset(144)] public ushort DeviceType;
    [FieldOffset(146)] public ushort FirmwareVersion;
    [FieldOffset(148)] public ushort RomCartridge;
    [FieldOffset(152)] public uint UnitId;
}

internal enum Key
{
    Unidentified = 0,
    Backquote,
    Backslash,
    BracketLeft,
    BracketRight,
    Comma,
    Digit0,
    Digit1,
    Digit2,
    Digit3,
    Digit4,
    Digit5,
    Digit6,
    Digit7,
    Digit8,
    Digit9,
    Equal,
    IntlBackslash,
    IntlRo,
    IntlYen,
    A,
    B,
    C,
    D,
    E,
    F,
    G,
    H,
    I,
    J,
    K,
    L,
    M,
    N,
    O,
    P,
    Q,
    R,
    S,
    T,
    U,
    V,
    W,
    X,
    Y,
    Z,
    Minus,
    Period,
    Quote,
    Semicolon,
    Slash,
    AltLeft,
    AltRight,
    Backspace,
    CapsLock,
    ContextMenu,
    ControlLeft,
    ControlRight,
    Enter,
    MetaLeft,
    MetaRight,
    ShiftLeft,
    ShiftRight,
    Space,
    Tab,
    Convert,
    KanaMode,
    NonConvert,
    Delete,
    End,
    Help,
    Home,
    Insert,
    PageDown,
    PageUp,
    ArrowDown,
    ArrowLeft,
    ArrowRight,
    ArrowUp,
    NumLock,
    Numpad0,
    Numpad1,
    Numpad2,
    Numpad3,
    Numpad4,
    Numpad5,
    Numpad6,
    Numpad7,
    Numpad8,
    Numpad9,
    NumpadAdd,
    NumpadBackspace,
    NumpadClear,
    NumpadClearEntry,
    NumpadComma,
    NumpadDecimal,
    NumpadDivide,
    NumpadEnter,
    NumpadEqual,
    NumpadMemoryAdd,
    NumpadMemoryClear,
    NumpadMemoryRecall,
    NumpadMemoryStore,
    NumpadMemorySubtract,
    NumpadMultiply,
    NumpadParenLeft,
    NumpadParenRight,
    NumpadSubtract,
    NumpadSeparator,
    NumpadUp,
    NumpadDown,
    NumpadRight,
    NumpadLeft,
    NumpadBegin,
    NumpadHome,
    NumpadEnd,
    NumpadInsert,
    NumpadDelete,
    NumpadPageUp,
    NumpadPageDown,
    Escape,
    F1,
    F2,
    F3,
    F4,
    F5,
    F6,
    F7,
    F8,
    F9,
    F10,
    F11,
    F12,
    F13,
    F14,
    F15,
    F16,
    F17,
    F18,
    F19,
    F20,
    F21,
    F22,
    F23,
    F24,
}

[Flags]
internal enum Mods : ushort
{
    None = 0,
    Shift = 1 << 0,
    Ctrl = 1 << 1,
    Alt = 1 << 2,
    Super = 1 << 3,
    CapsLock = 1 << 4,
    NumLock = 1 << 5,
}
