using Fleet.Shared.Aidlc.Enums;

namespace Fleet.Shared.Aidlc;

public static class ProfilePrefix
{
    public const char Separator = ':';

    public static (Profile? Profile, string Task) Split(string prompt)
    {
        var trimmed = prompt.TrimStart();
        var colon = trimmed.IndexOf(Separator);

        if (colon <= 0 || Words.Parse<Profile>(trimmed[..colon]) is not { } profile)
        {
            return (null, prompt);
        }

        return (profile, trimmed[(colon + 1)..].Trim());
    }

    public static string For(Profile profile) => Words.Of(profile) + Separator;
}
