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
    // Delivery tags are scoped to the channel that delivered the message, so
    // an ack must go back on that exact channel. The delivering channel flows
    // to AckAsync through the handler's async context; a tag acked on any
    // other channel is rejected by the broker (or acks a different message).
    private static readonly AsyncLocal<IChannel?> DeliveryChannel = new();

    private readonly LazyChannelFactory _channelFactory;
    private readonly ILogger<RabbitConsumerBroker> _logger;

    public RabbitConsumerBroker(LazyChannelFactory channelFactory, ILogger<RabbitConsumerBroker> logger)
    {
        _channelFactory = channelFactory;
        _logger = logger;
    }

    public async Task AckAsync(ulong deliveryTag)
    {
        var channel = DeliveryChannel.Value;
        if (channel is not { IsOpen: true })
        {
            // The delivering channel is gone: the broker has already requeued
            // the message and will redeliver it; the idempotent handler skips it.
            _logger.LogWarning(
                "Cannot ack delivery {DeliveryTag}: its channel is closed; the broker will redeliver it.",
                deliveryTag);
            return;
        }

        await channel.BasicAckAsync(deliveryTag, multiple: false).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs <paramref name="pipeline"/> as the handling of a delivery that
    /// arrived on <paramref name="channel"/>: every <see cref="AckAsync"/>
    /// inside it acks on that channel.
    /// </summary>
    public static async Task RunDeliveryAsync(IChannel channel, Func<Task> pipeline)
    {
        DeliveryChannel.Value = channel;
        await pipeline().ConfigureAwait(false);
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
        var consumer = new DeliveryConsumer(channel, handler, _logger);

        // Completed when the broker shuts the channel down — the caller
        // (the consumer's retry loop) must not re-subscribe until then,
        // otherwise every iteration leaks a new BasicConsume on the same
        // shared channel (unbounded consumer growth).
        var channelShutdown = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        consumer.ChannelShutdown = channelShutdown;

        // Prefetch a bounded batch (D2): with autoAck: false the broker must
        // not pile unacknowledged deliveries onto this consumer while a slow
        // apply (EF round-trip) is in flight.
        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 10, global: false, ct).ConfigureAwait(false);

        // autoAck: false — a delivery is only acknowledged after the
        // handler's pipeline completes, so a crash redelivers it.
        await channel.BasicConsumeAsync(
            queue,
            autoAck: false,
            consumerTag: string.Empty,
            noLocal: false,
            exclusive: false,
            arguments: null,
            consumer,
            ct).ConfigureAwait(false);
        // Link the shutdown TCS to the caller's token: a host shutdown while
        // the channel is still healthy (the normal compose stop path) must
        // end ConsumeAsync — without this the TCS never completes and the
        // consume blocks until the host is force-killed. The await below
        // then throws OperationCanceledException, which the caller's retry
        // loop already handles (it breaks out and stops the service).
        using var cancellation = ct.Register(() => channelShutdown.TrySetCanceled());

        // Block until the channel is shut down so the caller's retry loop
        // only re-subscribes after a real failure. Returning immediately
        // would leak a new BasicConsume on the same shared channel on
        // every loop iteration (unbounded consumer growth).
        await channelShutdown.Task.ConfigureAwait(false);
    }

    private sealed class DeliveryConsumer : IAsyncBasicConsumer
    {
        private readonly IChannel _channel;
        private readonly Func<ulong, ReadOnlyMemory<byte>, IDictionary<string, object?>?, Task> _handler;
        private readonly ILogger _logger;

        public TaskCompletionSource? ChannelShutdown { get; set; }

        public DeliveryConsumer(
            IChannel channel,
            Func<ulong, ReadOnlyMemory<byte>, IDictionary<string, object?>?, Task> handler,
            ILogger logger)
        {
            _channel = channel;
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

            // The client reuses the body buffer once this callback returns,
            // and the pipeline below outlives it: it must own a copy.
            var bodyCopy = body.ToArray();

            // The broker dispatches synchronously; the pipeline (which
            // always ends in an ack) runs on the pool without blocking the
            // channel's dispatch loop.
            _ = Task.Run(async () =>
            {
                try
                {
                    await RunDeliveryAsync(_channel, () => _handler(deliveryTag, bodyCopy, headers))
                        .ConfigureAwait(false);
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
        {
            ChannelShutdown?.TrySetResult();
            return Task.CompletedTask;
        }

        public IChannel? Channel { get; set; }
    }
}
