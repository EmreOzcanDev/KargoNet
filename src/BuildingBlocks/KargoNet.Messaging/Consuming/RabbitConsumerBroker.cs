using KargoNet.Messaging.Infra;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Framing;

namespace KargoNet.Messaging.Consuming;

/// <summary>
/// Production <see cref="IConsumerBroker"/> over a live RabbitMQ channel
/// (RabbitMQ.Client 7.2.2, pinned by Phase 2 / plan 04-02). Lazy: nothing
/// connects at construction — the channel (and with it the connection)
/// opens on the first Ack/Publish/Consume call through the plan 04-02
/// <see cref="LazyChannelFactory"/>, so the host starts and serves traffic
/// without a reachable broker (live consume is a Phase 6 deferral).
/// </summary>
public sealed class RabbitConsumerBroker : IConsumerBroker
{
    private readonly LazyChannelFactory _channelFactory;
    private readonly ILogger<RabbitConsumerBroker> _logger;

    public RabbitConsumerBroker(LazyChannelFactory channelFactory, ILogger<RabbitConsumerBroker> logger)
    {
        _channelFactory = channelFactory;
        _logger = logger;
    }

    public async Task AckAsync(ulong deliveryTag)
    {
        // Per-operation channel (caller disposes): a shared cached channel
        // could have been shut down by the broker between deliveries.
        var channel = await _channelFactory.CreateChannelAsync().ConfigureAwait(false);
        try
        {
            await channel.BasicAckAsync(deliveryTag, multiple: false).ConfigureAwait(false);
        }
        finally
        {
            await channel.DisposeAsync();
        }
    }

    public async Task PublishAsync(
        string exchange,
        string routingKey,
        ReadOnlyMemory<byte> body,
        IDictionary<string, object?>? headers)
    {
        var channel = await _channelFactory.CreateChannelAsync().ConfigureAwait(false);
        try
        {
            var properties = new BasicProperties
            {
                // The wire format always carries a header table — an empty one
                // when the caller publishes without headers.
                Headers = headers?.ToDictionary(kv => kv.Key, kv => kv.Value)
                        ?? new Dictionary<string, object?>()
            };

            await channel.BasicPublishAsync(
                exchange, routingKey, mandatory: false, properties, body).ConfigureAwait(false);
        }
        finally
        {
            await channel.DisposeAsync();
        }
    }

    public async Task ConsumeAsync(
        string queue,
        Func<ulong, ReadOnlyMemory<byte>, IDictionary<string, object?>?, Task> handler,
        CancellationToken ct)
    {
        var channel = await _channelFactory.GetChannelAsync().ConfigureAwait(false);
        var consumer = new DeliveryConsumer(handler, _logger);

        // Prefetch a bounded batch (D2): with autoAck: false the broker must
        // not pile unacknowledged deliveries onto this consumer while a slow
        // apply (EF round-trip) is in flight.
        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 10, global: false, ct).ConfigureAwait(false);

        // autoAck: false — a delivery is only acknowledged after the
        await channel.BasicConsumeAsync(
            queue,
            autoAck: false,
            consumerTag: string.Empty,
            noLocal: false,
            exclusive: false,
            arguments: null,
            consumer,
            ct).ConfigureAwait(false);
    }

    private sealed class DeliveryConsumer : IAsyncBasicConsumer
    {
        private readonly Func<ulong, ReadOnlyMemory<byte>, IDictionary<string, object?>?, Task> _handler;
        private readonly ILogger _logger;

        public DeliveryConsumer(
            Func<ulong, ReadOnlyMemory<byte>, IDictionary<string, object?>?, Task> handler,
            ILogger logger)
        {
            _handler = handler;
            _logger = logger;
        }

        public Task HandleBasicDeliverAsync(
            string consumerTag,
            ulong deliveryTag,
            bool redelivered,
            string exchange,
            string routingKey,
            IReadOnlyBasicProperties basicProperties,
            ReadOnlyMemory<byte> body,
            CancellationToken ct)
        {
            IDictionary<string, object?>? headers = null;
            if (basicProperties.Headers is { } dictionary)
            {
                headers = new Dictionary<string, object?>(dictionary.Count);
                foreach (var entry in dictionary)
                    headers[entry.Key] = entry.Value;
            }

            // The broker dispatches synchronously; the pipeline (which
            // always ends in an ack) runs on the pool without blocking the
            // channel's dispatch loop.
            _ = Task.Run(async () =>
            {
                try
                {
                    await _handler(deliveryTag, body, headers).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    // The pipeline contract is "every delivery ends in an
                    // ack"; an escape is a bug, but it must not kill the
                    // broker's consume callback.
                    _logger.LogError(
                        exception,
                        "Delivery {DeliveryTag} handler escaped without acking.",
                        deliveryTag);
                }
            });

            return Task.CompletedTask;
        }

        public Task HandleBasicConsumeOkAsync(string consumerTag, CancellationToken ct)
            => Task.CompletedTask;

        public Task HandleBasicCancelAsync(string consumerTag, CancellationToken ct)
            => Task.CompletedTask;

        public Task HandleBasicCancelOkAsync(string consumerTag, CancellationToken ct)
            => Task.CompletedTask;

        public Task HandleChannelShutdownAsync(object sender, ShutdownEventArgs e)
            => Task.CompletedTask;

        public IChannel? Channel { get; set; }
    }
}
