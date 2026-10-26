namespace Fleet.Platform.Uninstall;

public interface IUserPathVariable
{
    string? Get();

    void Set(string value);
}
