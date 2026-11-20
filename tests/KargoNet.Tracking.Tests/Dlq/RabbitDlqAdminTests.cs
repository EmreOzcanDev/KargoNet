using System.Text;
using KargoNet.Messaging.Infra;
using KargoNet.Messaging.Topology;
using KargoNet.Tracking.Api.Dlq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Xunit;

namespace KargoNet.Tracking.Tests.Dlq
{
    public class RabbitDlqAdminTests
    {
        [Fact]
        public async Task CountAsync_returns_the_passive_declare_message_count()
        {
            var channel = new FakeChannel();
            channel.PassiveMessageCount = 42;
            var admin = new RabbitDlqAdmin(new FakeChannelFactory(channel), NullLogger<RabbitDlqAdmin>.Instance);

            var count = await admin.CountAsync();

            Assert.Equal(42, count);
            Assert.Single(channel.PassiveDeclares);
            Assert.Equal(RabbitTopology.Dlq, channel.PassiveDeclares[0]);
        }

        [Fact]
        public async Task ReplayAsync_republishes_every_body_to_the_exchange_without_a_retry_header_and_returns_the_count()
        {
            var channel = new FakeChannel();
            var one = Encoding.UTF8.GetBytes("body-1");
            var two = Encoding.UTF8.GetBytes("body-2");
            var three = Encoding.UTF8.GetBytes("body-3");
            channel.Deliveries = [one, two, three];
            var admin = new RabbitDlqAdmin(new FakeChannelFactory(channel), NullLogger<RabbitDlqAdmin>.Instance);

            var moved = await admin.ReplayAsync(CancellationToken.None);

            Assert.Equal(3, moved);
            Assert.True(channel.Disposed);
            Assert.Equal(3, channel.Published.Count);
            foreach (var publish in channel.Published)
            {
                Assert.Equal(RabbitTopology.Exchange, publish.exchange);
                Assert.Equal(RabbitTopology.ShipmentRoutingKey, publish.routingKey);
                // A fresh delivery: NO x-retry-count header.
                Assert.True(publish.properties is null
                    || publish.properties.Headers is null
                    || !publish.properties.Headers.ContainsKey("x-retry-count"));
            }
            Assert.Equal(one, channel.Published[0].body);
            Assert.Equal(two, channel.Published[1].body);
            Assert.Equal(three, channel.Published[2].body);
        }

        [Fact]
        public async Task ReplayAsync_on_an_empty_dlq_returns_zero()
        {
            var channel = new FakeChannel();
            var admin = new RabbitDlqAdmin(new FakeChannelFactory(channel), NullLogger<RabbitDlqAdmin>.Instance);

            var moved = await admin.ReplayAsync(CancellationToken.None);

            Assert.Equal(0, moved);
            Assert.True(channel.Disposed);
            Assert.Empty(channel.Published);
        }

        [Fact]
        public async Task ReplayAsync_stops_on_cancellation_and_disposes_the_channel()
        {
            var channel = new FakeChannel();
            channel.Deliveries = [Encoding.UTF8.GetBytes("body-1"), Encoding.UTF8.GetBytes("body-2")];
            var admin = new RabbitDlqAdmin(new FakeChannelFactory(channel), NullLogger<RabbitDlqAdmin>.Instance);

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAsync<OperationCanceledException>(() => admin.ReplayAsync(cts.Token));
            Assert.True(channel.Disposed);
            Assert.Empty(channel.Published);
        }

        /// <summary><see cref="LazyChannelFactory"/> wired to a canned channel (no broker).</summary>
        private sealed class FakeChannelFactory : LazyChannelFactory
        {
            private readonly IChannel _channel;

            public FakeChannelFactory(IChannel channel)
                : base(new ConfigurationBuilder().Build())
                => _channel = channel;

            public override Task<IChannel> GetChannelAsync() => Task.FromResult(_channel);

            public override Task<IChannel> CreateChannelAsync() => Task.FromResult(_channel);
        }

        /// <summary>
        /// Hand-rolled <see cref="IChannel"/> covering the three calls
        /// <see cref="RabbitDlqAdmin"/> makes; everything else throws.
        /// </summary>
        private sealed class FakeChannel : IChannel
        {
            public int PassiveMessageCount { get; set; }
            public List<ReadOnlyMemory<byte>> Deliveries { get; set; } = [];
            public List<string> PassiveDeclares { get; } = [];
            public List<(string exchange, string routingKey, IReadOnlyBasicProperties? properties, ReadOnlyMemory<byte> body)> Published { get; } = [];

            public bool Disposed { get; private set; }

            private int _deliveryIndex;

            public Task<QueueDeclareOk> QueueDeclarePassiveAsync(string queue, CancellationToken cancellationToken = default)
            {
                PassiveDeclares.Add(queue);
                return Task.FromResult(new QueueDeclareOk(queue, (uint)PassiveMessageCount, 0));
            }

            public Task<BasicGetResult?> BasicGetAsync(string queue, bool autoAck, CancellationToken cancellationToken = default)
            {
                Assert.Equal(RabbitTopology.Dlq, queue);
                if (_deliveryIndex >= Deliveries.Count)
                    return Task.FromResult<BasicGetResult?>(null);

                var body = Deliveries[_deliveryIndex++];
                return Task.FromResult<BasicGetResult?>(new BasicGetResult(
                    (ulong)_deliveryIndex, false, "", "", 0u, null!, body));
            }

            public ValueTask BasicPublishAsync<TProperties>(string exchange, string routingKey, bool mandatory, TProperties? basicProperties, ReadOnlyMemory<byte> body, CancellationToken cancellationToken = default)
                where TProperties : IReadOnlyBasicProperties, IAmqpHeader
            {
                Published.Add((exchange, routingKey, basicProperties, body));
                return ValueTask.CompletedTask;
            }

            public ValueTask BasicPublishAsync<TProperties>(CachedString exchange, CachedString routingKey, bool mandatory, TProperties? basicProperties, ReadOnlyMemory<byte> body, CancellationToken cancellationToken = default)
                where TProperties : IReadOnlyBasicProperties, IAmqpHeader
                => throw new NotImplementedException();

            // --- members the DLQ admin never touches ---------------------------------

            public ValueTask BasicAckAsync(ulong deliveryTag, bool multiple, CancellationToken cancellationToken = default)
                => throw new NotImplementedException();


            public ValueTask BasicRejectAsync(ulong deliveryTag, bool requeue, CancellationToken cancellationToken = default)
                => throw new NotImplementedException();
            public ValueTask BasicNackAsync(ulong deliveryTag, bool multiple, bool requeue, CancellationToken cancellationToken = default)
                => throw new NotImplementedException();

            public Task<string> BasicConsumeAsync(string queue, bool autoAck, string consumerTag, bool noLocal, bool exclusive, IDictionary<string, object?>? arguments, IAsyncBasicConsumer consumer, CancellationToken cancellationToken = default)
                => throw new NotImplementedException();

            public Task BasicCancelAsync(string consumerTag, bool noWait = false, CancellationToken cancellationToken = default)
                => throw new NotImplementedException();


            public Task BasicQosAsync(uint prefetchSize, ushort prefetchCount, bool global, CancellationToken cancellationToken = default)
                => throw new NotImplementedException();

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
