using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

// Terminal.Gui.Drawing.Attribute collides with System.Attribute, which
// ImplicitUsings brings in.
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Fleet.Ui;

/// <summary>
/// fleet's styling system: one palette, a handful of named schemes, and factory
/// methods for every kind of view fleet builds.
///
/// The rule this exists to enforce: <b>no view sets a colour or a border style of
/// its own.</b> Views ask for a Screen, a Modal, a Panel, a Primary button, and
/// they come back already consistent. Adding a new dialog therefore cannot drift
/// from the rest of the application, because there is no per-view styling to get
/// wrong.
///
/// Palette is Catppuccin Mocha, carried over from the predecessor so agent status
/// colours will line up when phase 2 adds them.
/// </summary>
public static class FleetTheme
{
    // --- palette -----------------------------------------------------------
    // Catppuccin Mocha. Named after the palette's own names rather than by usage,
    // so the mapping from palette to role stays visible below.

    private const string Crust = "#11111b";
    private const string Mantle = "#181825";
    private const string Base = "#1e1e2e";
    private const string Surface0 = "#313244";
    private const string Surface1 = "#45475a";
    private const string Overlay0 = "#6c7086";
    private const string Subtext0 = "#a6adc8";
    private const string Text = "#cdd6f4";
    private const string Blue = "#89b4fa";
    private const string Lavender = "#b4befe";
    private const string Green = "#a6e3a1";
    private const string Yellow = "#f9e2af";
    private const string Red = "#f38ba8";

    // --- scheme names ------------------------------------------------------

    public const string SchemeScreen = "fleet.screen";
    public const string SchemePanel = "fleet.panel";
    public const string SchemeAccent = "fleet.accent";
    public const string SchemeError = "fleet.error";
    public const string SchemeHint = "fleet.hint";

    /// <summary>Every fleet window uses the same border, so it is stated once.</summary>
    public const LineStyle Border = LineStyle.Rounded;

    /// <summary>
    /// Registers fleet's schemes. Call once, after the application is initialised
    /// and before any view is built.
    /// </summary>
    public static void Register()
    {
        // Screen: the ordinary background. Focus lifts to a surface tone rather
        // than inverting, which is what stops a selected list row from flashing.
        SchemeManager.AddScheme(SchemeScreen, new Scheme
        {
            Normal = Attr(Text, Base),
            HotNormal = Attr(Lavender, Base),
            Focus = Attr(Text, Surface1),
            HotFocus = Attr(Lavender, Surface1),
            Active = Attr(Text, Surface0),
            HotActive = Attr(Lavender, Surface0),
            Highlight = Attr(Blue, Surface0),
            Editable = Attr(Text, Surface0),
            ReadOnly = Attr(Subtext0, Base),
            Disabled = Attr(Overlay0, Base),
        });

        // Panel: frames sit a shade darker than the screen, which is what gives
        // the dashboard depth without drawing extra lines.
        SchemeManager.AddScheme(SchemePanel, new Scheme
        {
            Normal = Attr(Text, Mantle),
            HotNormal = Attr(Lavender, Mantle),
            Focus = Attr(Text, Surface1),
            HotFocus = Attr(Lavender, Surface1),
            Active = Attr(Text, Surface0),
            HotActive = Attr(Lavender, Surface0),
            Highlight = Attr(Blue, Surface0),
            Editable = Attr(Text, Surface0),
            ReadOnly = Attr(Subtext0, Mantle),
            Disabled = Attr(Overlay0, Mantle),
        });

        // Accent: the one default action per screen. Inverted so it reads as a
        // button rather than as text.
        SchemeManager.AddScheme(SchemeAccent, new Scheme
        {
            Normal = Attr(Crust, Blue),
            HotNormal = Attr(Crust, Lavender),
            Focus = Attr(Crust, Lavender),
            HotFocus = Attr(Crust, Green),
            Active = Attr(Crust, Lavender),
            HotActive = Attr(Crust, Green),
            Highlight = Attr(Crust, Green),
            Editable = Attr(Crust, Blue),
            ReadOnly = Attr(Crust, Blue),
            Disabled = Attr(Overlay0, Surface0),
        });

        SchemeManager.AddScheme(SchemeError, new Scheme
        {
            Normal = Attr(Red, Base),
            HotNormal = Attr(Yellow, Base),
            Focus = Attr(Red, Surface1),
            HotFocus = Attr(Yellow, Surface1),
            Active = Attr(Red, Surface0),
            HotActive = Attr(Yellow, Surface0),
            Highlight = Attr(Yellow, Surface0),
            Editable = Attr(Red, Base),
            ReadOnly = Attr(Red, Base),
            Disabled = Attr(Overlay0, Base),
        });

        SchemeManager.AddScheme(SchemeHint, new Scheme
        {
            Normal = Attr(Overlay0, Base),
            HotNormal = Attr(Blue, Base),
            Focus = Attr(Subtext0, Base),
            HotFocus = Attr(Blue, Base),
            Active = Attr(Subtext0, Base),
            HotActive = Attr(Blue, Base),
            Highlight = Attr(Blue, Base),
            Editable = Attr(Overlay0, Base),
            ReadOnly = Attr(Overlay0, Base),
            Disabled = Attr(Overlay0, Base),
        });
    }

    // --- factories ---------------------------------------------------------

    /// <summary>A full-terminal window. One per command.</summary>
    public static Window Screen(string title) => new()
    {
        Title = $" {title} ",
        BorderStyle = Border,
        SchemeName = SchemeScreen,
    };

    /// <summary>A centred window used as a modal.</summary>
    public static Window Modal(string title, int width, int height) => new()
    {
        Title = $" {title} ",
        X = Pos.Center(),
        Y = Pos.Center(),
        Width = width,
        Height = height,
        BorderStyle = Border,
        SchemeName = SchemeScreen,
    };

    /// <summary>A titled region inside a screen.</summary>
    public static FrameView Panel(string title) => new()
    {
        Title = $" {title} ",
        BorderStyle = Border,
        SchemeName = SchemePanel,
    };

    public static ListView Rows() => new()
    {
        X = 0,
        Y = 0,
        Width = Dim.Fill(),
        Height = Dim.Fill(),
        SchemeName = SchemePanel,
    };

    public static Label Caption(Pos x, Pos y, string text) => new()
    {
        X = x,
        Y = y,
        Text = text,
        SchemeName = SchemeScreen,
    };

    public static TextField Field(Pos x, Pos y, string text = "") => new()
    {
        X = x,
        Y = y,
        Width = Dim.Fill(2),
        Text = text,
        SchemeName = SchemeScreen,
    };

    public static CheckBox Toggle(Pos x, Pos y, string text) => new()
    {
        X = x,
        Y = y,
        Text = text,
        SchemeName = SchemeScreen,
    };

    /// <summary>The default action. At most one per window.</summary>
    public static Button Primary(Pos x, Pos y, string text) => new()
    {
        X = x,
        Y = y,
        Text = text,
        IsDefault = true,
        SchemeName = SchemeAccent,
    };

    public static Button Secondary(Pos x, Pos y, string text) => new()
    {
        X = x,
        Y = y,
        Text = text,
        SchemeName = SchemeScreen,
    };

    /// <summary>An inline error line. Starts empty; set Text to show a problem.</summary>
    public static Label ErrorText(Pos x, Pos y) => new()
    {
        X = x,
        Y = y,
        Width = Dim.Fill(2),
        Text = string.Empty,
        SchemeName = SchemeError,
    };

    /// <summary>
    /// The key hints, pinned to the bottom of a screen. Every screen gets one, in
    /// the same place, in the same muted tone.
    /// </summary>
    public static Label HintBar(string text) => new()
    {
        X = 1,
        Y = Pos.AnchorEnd(1),
        Width = Dim.Fill(1),
        Text = text,
        SchemeName = SchemeHint,
    };

    private static Attribute Attr(string foreground, string background)
        => new(foreground, background, TextStyle.None);
}
