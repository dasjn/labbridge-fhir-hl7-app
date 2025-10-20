namespace LabBridge.Core.Interfaces;

public interface IMllpServer
{
    Task StartAsync(int port, CancellationToken cancellationToken);
    Task StopAsync();
}
