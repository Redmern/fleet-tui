namespace RoyalPtySpike;

public enum ScanOutcome
{
    Forward,
    Swallowed,
    OpenMenu,
}

public sealed class PrefixScanner(byte prefix = 0x13, byte menu = 0x20)
{
    public bool Armed { get; private set; }

    public ScanOutcome Feed(byte b)
    {
        if (!Armed)
        {
            if (b == prefix)
            {
                Armed = true;
                return ScanOutcome.Swallowed;
            }

            return ScanOutcome.Forward;
        }

        Armed = false;

        if (b == menu)
        {
            return ScanOutcome.OpenMenu;
        }

        if (b == prefix)
        {
            Armed = true;
            return ScanOutcome.Swallowed;
        }

        return ScanOutcome.Forward;
    }
}
