namespace Fleet.Platform.Uninstall;

public sealed class UserPathVariable : IUserPathVariable
{
    private const string Name = "Path";

    public string? Get() =>
        OperatingSystem.IsWindows() ? Environment.GetEnvironmentVariable(Name, EnvironmentVariableTarget.User) : null;

    public void Set(string value)
    {
        if (OperatingSystem.IsWindows())
        {
            Environment.SetEnvironmentVariable(Name, value, EnvironmentVariableTarget.User);
        }
    }
}
