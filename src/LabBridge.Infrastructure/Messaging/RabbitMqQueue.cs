using System.Text;
using LabBridge.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace LabBridge.Infrastructure.Messaging;

public class RabbitMqQueue : IMessageQueue, IDisposable
{
    private readonly ILogger<RabbitMqQueue> _logger;
    private readonly IConfiguration _configuration;
    private IConnection? _connection;
    private IChannel? _channel;
    private AsyncEventingBasicConsumer? _consumer;

    private const string ExchangeName = "labbridge.hl7.exchange";
    private const string QueueName = "labbridge.hl7.queue";
    private const string RoutingKey = "hl7.message";
    private const string DeadLetterQueueName = "labbridge.hl7.dlq";

    public RabbitMqQueue(
        IConfiguration configuration,
        ILogger<RabbitMqQueue> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    private async Task EnsureConnectionAsync()
    {
        if (_connection != null && _connection.IsOpen)
            return;

        var hostname = _configuration.GetValue<string>("RabbitMq:Hostname") ?? "localhost";
        var port = _configuration.GetValue<int>("RabbitMq:Port", 5672);
        var username = _configuration.GetValue<string>("RabbitMq:Username") ?? "guest";
        var password = _configuration.GetValue<string>("RabbitMq:Password") ?? "guest";

        var factory = new ConnectionFactory
        {
            HostName = hostname,
            Port = port,
            UserName = username,
            Password = password
        };

        _connection = await factory.CreateConnectionAsync();
        _channel = await _connection.CreateChannelAsync();

        // Declare Dead Letter Queue first
        await _channel.QueueDeclareAsync(
            queue: DeadLetterQueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null);

        // Declare exchange
        await _channel.ExchangeDeclareAsync(
            exchange: ExchangeName,
            type: ExchangeType.Direct,
            durable: true,
            autoDelete: false,
            arguments: null);

        // Declare main queue with DLQ
        var queueArgs = new Dictionary<string, object?>
        {
            { "x-dead-letter-exchange", "" },
            { "x-dead-letter-routing-key", DeadLetterQueueName }
        };

        await _channel.QueueDeclareAsync(
            queue: QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: queueArgs);

        // Bind queue to exchange
        await _channel.QueueBindAsync(
            queue: QueueName,
            exchange: ExchangeName,
            routingKey: RoutingKey,
            arguments: null);

        _logger.LogInformation("RabbitMQ connection established: {Hostname}:{Port}", hostname, port);
    }

    public async Task PublishAsync(string hl7Message, string messageControlId)
    {
        await EnsureConnectionAsync();

        var messageBytes = Encoding.UTF8.GetBytes(hl7Message);
        var properties = new BasicProperties
        {
            Persistent = true,
            MessageId = messageControlId,
            Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
            ContentType = "text/plain",
            ContentEncoding = "utf-8"
        };

        await _channel!.BasicPublishAsync(
            exchange: ExchangeName,
            routingKey: RoutingKey,
            mandatory: false,
            basicProperties: properties,
            body: messageBytes);

        _logger.LogInformation("Published HL7 message to RabbitMQ: MessageControlId={MessageControlId}, Size={Size} bytes",
            messageControlId, messageBytes.Length);
    }

    public async Task StartConsumingAsync(Func<string, Task> messageHandler, CancellationToken cancellationToken)
    {
        await EnsureConnectionAsync();

        // Set QoS to process one message at a time
        await _channel!.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false);

        _consumer = new AsyncEventingBasicConsumer(_channel);

        _consumer.ReceivedAsync += async (sender, eventArgs) =>
        {
            var messageBody = Encoding.UTF8.GetString(eventArgs.Body.ToArray());
            var messageId = eventArgs.BasicProperties.MessageId ?? "UNKNOWN";

            try
            {
                _logger.LogInformation("Processing message from queue: MessageId={MessageId}", messageId);

                await messageHandler(messageBody);

                // Acknowledge message after successful processing
                await _channel.BasicAckAsync(deliveryTag: eventArgs.DeliveryTag, multiple: false);

                _logger.LogInformation("Message processed successfully: MessageId={MessageId}", messageId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing message: MessageId={MessageId}", messageId);

                // Reject message and send to DLQ (don't requeue)
                await _channel.BasicNackAsync(deliveryTag: eventArgs.DeliveryTag, multiple: false, requeue: false);
            }
        };

        await _channel.BasicConsumeAsync(
            queue: QueueName,
            autoAck: false,
            consumer: _consumer);

        _logger.LogInformation("Started consuming messages from RabbitMQ queue: {QueueName}", QueueName);
    }

    public async Task StopConsumingAsync()
    {
        if (_consumer != null && _channel != null)
        {
            await _channel.CloseAsync();
            _logger.LogInformation("Stopped consuming messages from RabbitMQ");
        }
    }

    public void Dispose()
    {
        _channel?.Dispose();
        _connection?.Dispose();
    }
}
