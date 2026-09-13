using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenOnboarding.Application.Interfaces;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace OpenOnboarding.Infrastructure.EventBus;

/// <summary>
/// Fans session events out to every API instance over a RabbitMQ fanout exchange.
/// <para>
/// Each instance declares its own exclusive, auto-delete queue bound to the shared exchange, so a
/// published event reaches every running instance and leaves nothing behind when one stops.
/// Messages are transient: a session event is only useful to a stream that is open right now, and
/// an instance that was down missed the stream too.
/// </para>
/// </summary>
public sealed class RabbitMqSessionEventTransport : ISessionEventTransport
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string _connectionString;
    private readonly string _exchangeName;
    private readonly string _clientProvidedName;
    private readonly ILogger<RabbitMqSessionEventTransport> _logger;

    private IConnection? _connection;
    private IChannel? _channel;

    /// <param name="clientProvidedName">
    /// Name this connection reports to the broker. Defaults to one naming the host, so the
    /// management UI identifies which replica owns which queue - without it every replica shows up
    /// as an anonymous connection and a stuck consumer cannot be traced back to an instance.
    /// </param>
    public RabbitMqSessionEventTransport(
        string connectionString,
        string exchangeName,
        ILogger<RabbitMqSessionEventTransport> logger,
        string? clientProvidedName = null)
    {
        _connectionString = connectionString;
        _exchangeName = exchangeName;
        _logger = logger;
        _clientProvidedName = clientProvidedName ?? $"open-onboarding-session-events:{Environment.MachineName}";
    }

    /// <summary>Wire envelope. Kept separate from <see cref="SessionEvent"/> so the session id travels with it.</summary>
    internal sealed record Envelope(Guid SessionId, string EventType, string PayloadJson, DateTimeOffset Timestamp)
    {
        public static Envelope From(Guid sessionId, SessionEvent evt)
            => new(sessionId, evt.EventType, evt.PayloadJson, evt.Timestamp);

        public SessionEvent ToSessionEvent() => new(EventType, PayloadJson, Timestamp);
    }

    internal static byte[] Serialize(Guid sessionId, SessionEvent evt)
        => JsonSerializer.SerializeToUtf8Bytes(Envelope.From(sessionId, evt), JsonOptions);

    internal static Envelope? Deserialize(ReadOnlySpan<byte> body)
        => JsonSerializer.Deserialize<Envelope>(body, JsonOptions);

    /// <summary>
    /// Hands one received message to <paramref name="handler"/>, swallowing anything it or the
    /// deserialiser throws.
    /// <para>
    /// A single malformed or unhandleable message must never tear down the consumer: this channel
    /// carries every session on this instance, so one poison body would silently stop progress
    /// reaching every open SSE stream - the exact failure the distributed transport exists to
    /// prevent. Extracted from the consumer callback so it can be tested without a broker.
    /// </para>
    /// </summary>
    internal static async Task DeliverAsync(
        ReadOnlyMemory<byte> body,
        Func<Guid, SessionEvent, Task> handler,
        ILogger logger)
    {
        try
        {
            var envelope = Deserialize(body.Span);
            if (envelope is not null)
                await handler(envelope.SessionId, envelope.ToSessionEvent());
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to deliver a session event received from RabbitMQ.");
        }
    }

    public async Task StartAsync(Func<Guid, SessionEvent, Task> handler, CancellationToken cancellationToken = default)
    {
        // Recovery is stated rather than inherited from the client defaults. On a dropped
        // connection the client reconnects and replays the topology - the exclusive queue, its
        // binding and the consumer - so delivery resumes without this instance restarting. Left
        // implicit, a future client default could turn this off and the only symptom would be SSE
        // streams that stay open and deliver nothing.
        var factory = new ConnectionFactory
        {
            Uri = new Uri(_connectionString),
            AutomaticRecoveryEnabled = true,
            TopologyRecoveryEnabled = true,
            NetworkRecoveryInterval = TimeSpan.FromSeconds(5),
            ClientProvidedName = _clientProvidedName
        };
        _connection = await factory.CreateConnectionAsync(cancellationToken);
        _channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);

        await _channel.ExchangeDeclareAsync(
            _exchangeName, ExchangeType.Fanout, durable: true, autoDelete: false,
            cancellationToken: cancellationToken);

        // Exclusive + auto-delete: this queue belongs to this instance and disappears with it.
        var queue = await _channel.QueueDeclareAsync(
            queue: string.Empty, durable: false, exclusive: true, autoDelete: true,
            cancellationToken: cancellationToken);

        await _channel.QueueBindAsync(
            queue.QueueName, _exchangeName, routingKey: string.Empty,
            cancellationToken: cancellationToken);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += (_, args) => DeliverAsync(args.Body, handler, _logger);

        await _channel.BasicConsumeAsync(
            queue.QueueName, autoAck: true, consumer, cancellationToken: cancellationToken);

        _logger.LogInformation(
            "Subscribed to session event exchange {Exchange} on queue {Queue}.",
            _exchangeName, queue.QueueName);
    }

    public async Task BroadcastAsync(
        Guid sessionId,
        SessionEvent sessionEvent,
        CancellationToken cancellationToken = default)
    {
        if (_channel is null)
            throw new InvalidOperationException("StartAsync must be called before broadcasting session events.");

        var props = new BasicProperties { Persistent = false, ContentType = "application/json" };

        await _channel.BasicPublishAsync(
            _exchangeName,
            routingKey: string.Empty,
            mandatory: false,
            basicProperties: props,
            body: Serialize(sessionId, sessionEvent),
            cancellationToken: cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null)
            await _channel.DisposeAsync();

        if (_connection is not null)
            await _connection.DisposeAsync();
    }
}
