namespace Fleet.Platform.Uninstall.Models;

public sealed record UninstallEnvironment(
    string Executable,
    string Home,
    string ConfigHome,
    bool Windows,
    Func<string, string?> Variable)
{
    public static UninstallEnvironment ForThisMachine(string executable, string configHome) =>
        new(
            executable,
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            configHome,
            OperatingSystem.IsWindows(),
            Environment.GetEnvironmentVariable);

    public string Directory(string variable, params string[] underHome) =>
        Or(variable, Path.Combine([Home, .. underHome]));

    public string Or(string variable, string fallback)
    {
        var value = Variable(variable);

        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }
}
