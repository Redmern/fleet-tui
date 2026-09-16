namespace Fleet.Ports.Releases;

public interface IBinaryInstaller
{
    void Replace(string targetPath, byte[] content);
}
