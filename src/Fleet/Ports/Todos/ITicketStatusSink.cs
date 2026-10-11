namespace Fleet.Ports.Todos;

public interface ITicketStatusSink
{
    void Reported(string projectRoot, string orchestration, string status, string summary);
}
