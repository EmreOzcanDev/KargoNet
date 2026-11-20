using KargoNet.Messaging.Infra;
using Microsoft.Extensions.Configuration;
using RabbitMQ.Client.Events;
using RabbitMQ.Client;
using Xunit;

namespace KargoNet.Tracking.Tests.Messaging
{
    public class LazyChannelFactoryTests
    {
        [Fact]
        public async Task GetChannelAsync_caches_the_same_channel_while_it_is_open()
        {
            var factory = new FakeChannelFactory();

            var first = await factory.GetChannelAsync();
            var second = await factory.GetChannelAsync();

            Assert.Same(first, second);
            Assert.Single(factory.Opened);
        }

        [Fact]
        public async Task GetChannelAsync_reopens_after_the_broker_closes_the_channel()
        {
            var factory = new FakeChannelFactory();
            var first = await factory.GetChannelAsync();

            // Simulate a broker-side shutdown of the cached channel.
            ((FakeChannel)first).IsOpen = false;

            var second = await factory.GetChannelAsync();

            Assert.NotSame(first, second);
            Assert.True(((FakeChannel)second).IsOpen);
            Assert.Equal(2, factory.Opened.Count);
        }

        [Fact]
        public async Task CreateChannelAsync_returns_a_distinct_channel_for_every_call()
        {
            var factory = new FakeChannelFactory();

            var a = await factory.CreateChannelAsync();
            var b = await factory.CreateChannelAsync();

            Assert.NotSame(a, b);
            Assert.Equal(2, factory.Opened.Count);
        }

        private sealed class FakeChannelFactory : LazyChannelFactory
        {
            public List<IChannel> Opened { get; } = [];

            public FakeChannelFactory()
                : base(new ConfigurationBuilder().Build())
            {
            }

            protected override Task<IChannel> OpenChannelAsync()
            {
                var channel = (IChannel)new FakeChannel();
                Opened.Add(channel);
                return Task.FromResult(channel);
            }
        }

        /// <summary>
        /// Hand-rolled <see cref="IChannel"/> with a mutable
        /// <see cref="IChannel.IsOpen"/>; everything else throws.
        /// </summary>
        private sealed class FakeChannel : IChannel
        {
            public bool IsOpen { get; set; } = true;

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

            public Task<QueueDeclareOk> QueueDeclarePassiveAsync(string queue, CancellationToken cancellationToken = default)
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

            public Task<BasicGetResult> BasicGetAsync(string queue, bool autoAck, CancellationToken cancellationToken = default)
                => throw new NotImplementedException();

            public ValueTask BasicPublishAsync<TProperties>(string exchange, string routingKey, bool mandatory, TProperties? basicProperties, ReadOnlyMemory<byte> body, CancellationToken cancellationToken = default)
                where TProperties : IReadOnlyBasicProperties, IAmqpHeader
                => throw new NotImplementedException();

            public ValueTask BasicPublishAsync<TProperties>(CachedString exchange, CachedString routingKey, bool mandatory, TProperties? basicProperties, ReadOnlyMemory<byte> body, CancellationToken cancellationToken = default)
                where TProperties : IReadOnlyBasicProperties, IAmqpHeader
                => throw new NotImplementedException();

            public void Dispose()
            {
            }

            public ValueTask DisposeAsync()
                => ValueTask.CompletedTask;

            // --- properties -----------------------------------------------------------

            public int ChannelNumber => 0;
            public ShutdownEventArgs? CloseReason => null;
            public IAsyncBasicConsumer? DefaultConsumer { get; set; }
            public bool IsClosed => false;
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
