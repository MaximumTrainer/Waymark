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

    private const int MaxPublishAttempts = 3;

    public async Task PublishAsync(INotification notification, CancellationToken cancellationToken = default)
    {
        var routingKey = notification.GetType().Name;
        var payload = JsonSerializer.SerializeToUtf8Bytes(notification, notification.GetType(), _jsonOptions);
        var props = new BasicProperties { Persistent = true, ContentType = "application/json" };

        await PublishWithRetryAsync(
            () => _channel.BasicPublishAsync(_exchangeName, routingKey, false, props, payload, cancellationToken).AsTask(),
            _logger,
            routingKey,
            MaxPublishAttempts,
            attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)),
            cancellationToken);

        _logger.LogDebug("Published {EventType} to RabbitMQ exchange {Exchange}.", routingKey, _exchangeName);
    }

    /// <summary>
    /// Executes <paramref name="send"/> with exponential-backoff retry up to
    /// <paramref name="maxAttempts"/> times. Public for unit-testability.
    /// </summary>
    public static async Task PublishWithRetryAsync(
        Func<Task> send,
        ILogger logger,
        string routingKey,
        int maxAttempts,
        Func<int, TimeSpan> backoff,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await send();
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                var delay = backoff(attempt);
                logger.LogWarning(ex,
                    "Failed to publish {EventType} to RabbitMQ (attempt {Attempt}/{Max}). Retrying in {Delay}s.",
                    routingKey, attempt, maxAttempts, delay.TotalSeconds);
                await Task.Delay(delay, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to publish {EventType} to RabbitMQ after {Max} attempts.", routingKey, maxAttempts);
                throw;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _channel.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
