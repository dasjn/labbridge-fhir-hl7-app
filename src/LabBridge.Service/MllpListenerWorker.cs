using LabBridge.Core.Interfaces;

namespace LabBridge.Service;

public class MllpListenerWorker : BackgroundService
{
    private readonly IMllpServer _mllpServer;
    private readonly ILogger<MllpListenerWorker> _logger;
    private readonly IConfiguration _configuration;

    public MllpListenerWorker(
        IMllpServer mllpServer,
        ILogger<MllpListenerWorker> logger,
        IConfiguration configuration)
    {
        _mllpServer = mllpServer;
        _logger = logger;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var port = _configuration.GetValue<int>("MllpListener:Port", 2575);

        _logger.LogInformation("Starting MLLP Listener on port {Port}", port);

        try
        {
            await _mllpServer.StartAsync(port, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("MLLP Listener stopped gracefully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error in MLLP Listener");
            throw;
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping MLLP Listener...");

        await _mllpServer.StopAsync();

        await base.StopAsync(cancellationToken);
    }
}
