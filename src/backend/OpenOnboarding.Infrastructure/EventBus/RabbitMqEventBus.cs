using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using OpenOnboarding.Application.Interfaces;
using RabbitMQ.Client;

namespace OpenOnboarding.Infrastructure.EventBus;

/// <summary>
/// Out-of-process event bus using RabbitMQ. Publishes to a topic exchange.
/// Enable by setting EventBus:Type = "rabbitmq" in configuration.
/// </summary>
public sealed class RabbitMqEventBus : IEventBus, IAsyncDisposable
{
    private readonly ILogger<RabbitMqEventBus> _logger;
    private readonly IConnection _connection;
    private readonly IChannel _channel;
    private readonly string _exchangeName;
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public RabbitMqEventBus(IConnection connection, IChannel channel, string exchangeName, ILogger<RabbitMqEventBus> logger)
    {
        _connection = connection;
        _channel = channel;
        _exchangeName = exchangeName;
        _logger = logger;
    }

    public static async Task<RabbitMqEventBus> CreateAsync(string connectionString, string exchangeName, ILogger<RabbitMqEventBus> logger)
    {
        var factory = new ConnectionFactory { Uri = new Uri(connectionString) };
        var connection = await factory.CreateConnectionAsync();
        var channel = await connection.CreateChannelAsync();
        await channel.ExchangeDeclareAsync(exchangeName, ExchangeType.Topic, durable: true);
        return new RabbitMqEventBus(connection, channel, exchangeName, logger);
    }

    public async Task PublishAsync(INotification notification, CancellationToken cancellationToken = default)
    {
        var routingKey = notification.GetType().Name;
        var payload = JsonSerializer.SerializeToUtf8Bytes(notification, notification.GetType(), _jsonOptions);
        var props = new BasicProperties { Persistent = true, ContentType = "application/json" };

        try
        {
            await _channel.BasicPublishAsync(_exchangeName, routingKey, false, props, payload, cancellationToken);
            _logger.LogDebug("Published {EventType} to RabbitMQ exchange {Exchange}.", routingKey, _exchangeName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish {EventType} to RabbitMQ.", routingKey);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _channel.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
