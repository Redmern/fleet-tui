using Fleet.Ui.Constants;
using Terminal.Gui.Input;
using Terminal.Gui.Views;

namespace Fleet.Ui;

#pragma warning disable CS0618
public sealed class FleetTextArea : TextView
#pragma warning restore CS0618
{
    public FleetTextArea()
    {
        Multiline = true;
        EnterKeyAddsLine = true;
        TabKeyAddsTab = false;
        WordWrap = true;
        SchemeName = FleetSchemes.Screen;
    }

    public event EventHandler? Submitting;

    public event EventHandler? GoingBack;

    public event EventHandler? Cancelling;

    public string Value => Normalize(Text);

    public static string Normalize(string text) =>
        text.Replace("\r\n", "\n").Replace('\r', '\n');

    protected override bool OnKeyDown(Key key)
    {
        if (FleetKeys.Submits(key))
        {
            Submitting?.Invoke(this, EventArgs.Empty);
            return true;
        }

        if (key == FleetKeys.Cancel)
        {
            Cancelling?.Invoke(this, EventArgs.Empty);
            return true;
        }

        if (FleetKeys.GoesBack(key, Value))
        {
            GoingBack?.Invoke(this, EventArgs.Empty);
            return true;
        }

        return base.OnKeyDown(key);
    }
}
