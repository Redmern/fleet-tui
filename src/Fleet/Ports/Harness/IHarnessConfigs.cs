namespace Fleet.Ports.Harness;

public interface IHarnessConfigs
{
    IHarnessConfig For(string harness);
}
