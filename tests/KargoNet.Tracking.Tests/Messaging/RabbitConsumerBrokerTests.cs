using KargoNet.Messaging.Consuming;
using KargoNet.Messaging.Infra;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace KargoNet.Tracking.Tests.Messaging
{
    /// <summary>
    /// Plan 04-03: <see cref="RabbitConsumerBroker"/> is lazy (no channel on
    /// construction) and delegates ack / publish / consume to the underlying
    /// <see cref="IChannel"/> with auto-ack disabled.
    /// </summary>
    public class RabbitConsumerBrokerTests
    {
        [Fact]
        public async Task Constructor_does_not_fetch_a_channel()
        {
            var channel = new FakeChannel();
            var factory = new FakeChannelFactory(channel);

            _ = new RabbitConsumerBroker(factory, NullLogger<RabbitConsumerBroker>.Instance);

            Assert.Equal(0, factory.FetchCount);
        }

        [Fact]
        public async Task AckAsync_acks_single_delivery_without_multiple()
        {
            var channel = new FakeChannel();
            var broker = CreateBroker(channel);

            await broker.AckAsync(5);

            var (tag, multiple) = Assert.Single(channel.Acks);
            Assert.Equal(5ul, tag);
            Assert.False(multiple);
        }

        [Fact]
        public async Task PublishAsync_publishes_to_exchange_and_routing_key_with_headers()
        {
            var channel = new FakeChannel();
            var broker = CreateBroker(channel);
            var headers = new Dictionary<string, object?> { ["x-retry-count"] = 3, ["x-exception"] = "boom" };

            await broker.PublishAsync("ex.changes", "shipment.retry", new byte[] { 1, 2, 3 }, headers);

            var published = Assert.Single(channel.Publishes);
            Assert.Equal("ex.changes", published.Exchange);
            Assert.Equal("shipment.retry", published.RoutingKey);
            Assert.Equal([1, 2, 3], published.Body);
            Assert.Equal(3, published.Properties!.Headers!["x-retry-count"]);
            Assert.Equal("boom", published.Properties!.Headers!["x-exception"]);
        }

        [Fact]
        public async Task PublishAsync_without_headers_sends_empty_header_table()
        {
            var channel = new FakeChannel();
            var broker = CreateBroker(channel);

            await broker.PublishAsync("ex.changes", "shipment.main", new byte[] { 9 }, null);

            var published = Assert.Single(channel.Publishes);
            Assert.NotNull(published.Properties);
            Assert.Empty(published.Properties!.Headers!);
        }

        [Fact]
        public async Task ConsumeAsync_consumes_queue_with_manual_ack()
        {
            var channel = new FakeChannel();
            var broker = CreateBroker(channel);

            var consume = broker.ConsumeAsync("shipments.main", (_, _, _) => Task.CompletedTask, CancellationToken.None);

            // ConsumeAsync blocks until the channel shuts down; simulate the broker closing it.
            channel.SimulateChannelShutdown();
            await consume;

            var (queue, autoAck) = Assert.Single(channel.Consumes);
            Assert.Equal("shipments.main", queue);
            Assert.False(autoAck);
        }

        [Fact]
        public async Task ConsumeAsync_sets_prefetch_before_consuming()
        {
            var channel = new FakeChannel();
            var broker = CreateBroker(channel);

            var consume = broker.ConsumeAsync("shipments.main", (_, _, _) => Task.CompletedTask, CancellationToken.None);

            channel.SimulateChannelShutdown();
            await consume;

            var (size, count, global) = Assert.Single(channel.Qos);
            Assert.Equal(0u, size);
            Assert.Equal((ushort)10, count);
            Assert.False(global);
            Assert.Equal(["qos", "consume"], channel.Calls);
        }

        private static RabbitConsumerBroker CreateBroker(FakeChannel channel)
            => new(new FakeChannelFactory(channel), NullLogger<RabbitConsumerBroker>.Instance);

        /// <summary><see cref="LazyChannelFactory"/> wired to a canned channel (no broker).</summary>
        private sealed class FakeChannelFactory : LazyChannelFactory
        {
            private readonly IChannel _channel;

            public int FetchCount { get; private set; }

            public FakeChannelFactory(IChannel channel)
                : base(new ConfigurationBuilder().Build())
                => _channel = channel;

            public override Task<IChannel> GetChannelAsync()
            {
                FetchCount++;
                return Task.FromResult(_channel);
            }

            public override Task<IChannel> CreateChannelAsync()
                => Task.FromResult(_channel);
        }

        /// <summary>
        /// Hand-rolled <see cref="IChannel"/> recording the three calls
        /// <see cref="RabbitConsumerBroker"/> makes; everything else throws.
        /// </summary>
        private sealed class FakeChannel : IChannel
        {
            public List<(ulong tag, bool multiple)> Acks { get; } = [];

            public bool Disposed { get; private set; }

            public sealed record Published(
                string Exchange,
                string RoutingKey,
                IReadOnlyBasicProperties? Properties,
                byte[] Body);

            public List<Published> Publishes { get; } = [];

            public List<(string queue, bool autoAck)> Consumes { get; } = [];

            public List<(uint size, ushort count, bool global)> Qos { get; } = [];

            public List<string> Calls { get; } = [];

            public ValueTask BasicAckAsync(ulong deliveryTag, bool multiple, CancellationToken cancellationToken = default)
            {
                Acks.Add((deliveryTag, multiple));
                return ValueTask.CompletedTask;
            }

            public ValueTask BasicPublishAsync<TProperties>(string exchange, string routingKey, bool mandatory, TProperties? basicProperties, ReadOnlyMemory<byte> body, CancellationToken cancellationToken = default)
                where TProperties : IReadOnlyBasicProperties, IAmqpHeader
            {
                Publishes.Add(new Published(exchange, routingKey, basicProperties, body.ToArray()));
                return ValueTask.CompletedTask;
            }

            public ValueTask BasicPublishAsync<TProperties>(CachedString exchange, CachedString routingKey, bool mandatory, TProperties? basicProperties, ReadOnlyMemory<byte> body, CancellationToken cancellationToken = default)
                where TProperties : IReadOnlyBasicProperties, IAmqpHeader
                => throw new NotImplementedException();

            public Task<BasicGetResult> BasicGetAsync(string queue, bool autoAck, CancellationToken cancellationToken = default)
                => Task.FromException<BasicGetResult>(new NotImplementedException());

            public Task<QueueDeclareOk> QueueDeclarePassiveAsync(string queue, CancellationToken cancellationToken = default)
                => Task.FromException<QueueDeclareOk>(new NotImplementedException());

            public Task<string> BasicConsumeAsync(string queue, bool autoAck, string consumerTag, bool noLocal, bool exclusive, IDictionary<string, object?>? arguments, IAsyncBasicConsumer consumer, CancellationToken cancellationToken = default)
            {
                Consumes.Add((queue, autoAck));
                Calls.Add("consume");
                Consumer = consumer;
                return Task.FromResult(consumerTag);
            }

            /// <summary>Fires the consumer's channel-shutdown callback (simulates a broker-side close).</summary>
            public void SimulateChannelShutdown()
                => Consumer?.HandleChannelShutdownAsync(this, new ShutdownEventArgs(ShutdownStyle.Broker, 320, "simulated broker shutdown"));

            // --- members the broker does not touch -----------------------------------

            public ValueTask BasicRejectAsync(ulong deliveryTag, bool requeue, CancellationToken cancellationToken = default)
                => throw new NotImplementedException();
            public ValueTask BasicNackAsync(ulong deliveryTag, bool multiple, bool requeue, CancellationToken cancellationToken = default)
                => throw new NotImplementedException();

            public Task BasicCancelAsync(string consumerTag, bool noWait = false, CancellationToken cancellationToken = default)
                => throw new NotImplementedException();

            public Task BasicQosAsync(uint prefetchSize, ushort prefetchCount, bool global, CancellationToken cancellationToken = default)
            {
                Qos.Add((prefetchSize, prefetchCount, global));
                Calls.Add("qos");
                return Task.CompletedTask;
            }

            public ValueTask<ulong> GetNextPublishSequenceNumberAsync(CancellationToken cancellationToken = default)
                => throw new NotImplementedException();

            public Task CloseAsync(ushort replyCode, string replyText, bool abort, CancellationToken cancellationToken = default)
                => throw new NotImplementedException();

            public Task CloseAsync(ShutdownEventArgs reason, bool abort)
                => throw new NotImplementedException();

            public Task CloseAsync(ShutdownEventArgs reason, bool abort, CancellationToken cancellationToken = default)
                => throw new NotImplementedException();

            public Task<uint> ConsumerCountAsync(string queue, CancellationToken cancellationToken = default)
                => throw new NotImplementedException();

            public Task<uint> MessageCountAsync(string queue, CancellationToken cancellationToken = default)
                => throw new NotImplementedException();

            public Task ExchangeDeclareAsync(string exchange, string type, bool durable, bool autoDelete, IDictionary<string, object?>? arguments = null, bool passive = false, bool noWait = false, CancellationToken cancellationToken = default)
                => throw new NotImplementedException();

            public Task ExchangeDeclarePassiveAsync(string exchange, CancellationToken cancellationToken = default)
                => throw new NotImplementedException();

            public Task ExchangeDeleteAsync(string exchange, bool ifUnused = false, bool noWait = false, CancellationToken cancellationToken = default)
                => throw new NotImplementedException();

            public Task ExchangeBindAsync(string destination, string source, string routingKey, IDictionary<string, object?>? arguments = null, bool noWait = false, CancellationToken cancellationToken = default)
                => throw new NotImplementedException();

            public Task ExchangeUnbindAsync(string destination, string source, string routingKey, IDictionary<string, object?>? arguments = null, bool noWait = false, CancellationToken cancellationToken = default)
                => throw new NotImplementedException();

            public Task<QueueDeclareOk> QueueDeclareAsync(string queue, bool durable, bool exclusive, bool autoDelete, IDictionary<string, object?>? arguments = null, bool passive = false, bool noWait = false, CancellationToken cancellationToken = default)
                => throw new NotImplementedException();

            public Task<uint> QueueDeleteAsync(string queue, bool ifUnused, bool ifEmpty, bool noWait = false, CancellationToken cancellationToken = default)
                => throw new NotImplementedException();

            public Task<uint> QueuePurgeAsync(string queue, CancellationToken cancellationToken = default)
                => throw new NotImplementedException();

            public Task QueueBindAsync(string queue, string exchange, string routingKey, IDictionary<string, object?>? arguments = null, bool noWait = false, CancellationToken cancellationToken = default)
                => throw new NotImplementedException();

            public Task QueueUnbindAsync(string queue, string exchange, string routingKey, IDictionary<string, object?>? arguments = null, bool noWait = false, CancellationToken cancellationToken = default)
                => throw new NotImplementedException();

            public Task QueueUnbindAsync(string queue, string exchange, string routingKey, IDictionary<string, object?>? arguments, CancellationToken cancellationToken = default)
                => throw new NotImplementedException();

            public Task TxSelectAsync(CancellationToken cancellationToken = default)
                => throw new NotImplementedException();

            public Task TxCommitAsync(CancellationToken cancellationToken = default)
                => throw new NotImplementedException();

            public Task TxRollbackAsync(CancellationToken cancellationToken = default)
                => throw new NotImplementedException();

            public void Dispose() => Disposed = true;
            public ValueTask DisposeAsync()
            {
                Disposed = true;
                return ValueTask.CompletedTask;
            }

            // --- properties -----------------------------------------------------------

            public int ChannelNumber => 0;
            public ShutdownEventArgs? CloseReason => null;
            public IAsyncBasicConsumer? DefaultConsumer { get; set; }
            public bool IsClosed => false;
            public bool IsOpen => true;
            public string CurrentQueue => string.Empty;
            public TimeSpan ContinuationTimeout { get; set; }

            // --- events ---------------------------------------------------------------

            public event AsyncEventHandler<BasicAckEventArgs>? BasicAcksAsync
            {
                add => throw new NotImplementedException();
                remove => throw new NotImplementedException();
            }

            public event AsyncEventHandler<BasicNackEventArgs>? BasicNacksAsync
            {
                add => throw new NotImplementedException();
                remove => throw new NotImplementedException();
            }

            public event AsyncEventHandler<BasicReturnEventArgs>? BasicReturnAsync
            {
                add => throw new NotImplementedException();
                remove => throw new NotImplementedException();
            }

            public event AsyncEventHandler<CallbackExceptionEventArgs>? CallbackExceptionAsync
            {
                add => throw new NotImplementedException();
                remove => throw new NotImplementedException();
            }

            public event AsyncEventHandler<ShutdownEventArgs>? ChannelShutdownAsync
            {
                add => throw new NotImplementedException();
                remove => throw new NotImplementedException();
            }

            public event AsyncEventHandler<FlowControlEventArgs>? FlowControlAsync
            {
                add => throw new NotImplementedException();
                remove => throw new NotImplementedException();
            }
        }
    }
}
