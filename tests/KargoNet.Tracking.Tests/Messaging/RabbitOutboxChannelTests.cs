using System.Text;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using KargoNet.Messaging.Publishing;

namespace KargoNet.Tracking.Tests.Messaging;

public class RabbitOutboxChannelTests
{
    /// <summary>
    /// Broker-free <see cref="IChannel"/> stub: hands out monotonically
    /// increasing publish tags and lets the test fire ack frames directly.
    /// </summary>
    private sealed class FakeChannel : IChannel
    {
        private ulong _tagCounter;

        private IAsyncBasicConsumer? _defaultConsumer;

        public FakeChannel(TimeSpan continuationTimeout)
            => ContinuationTimeout = continuationTimeout;

        public int ChannelNumber => 0;

        public ShutdownEventArgs? CloseReason => null;

        public IAsyncBasicConsumer? DefaultConsumer
        {
            get => _defaultConsumer;
            set => _defaultConsumer = value;
        }

        public bool IsClosed => false;

        public bool IsOpen => true;

        public string? CurrentQueue => null;

        public TimeSpan ContinuationTimeout { get; set; }

        public List<ulong> PublishedTags { get; } = [];

#pragma warning disable CS0067 // interface events implemented but never raised

        public event AsyncEventHandler<BasicAckEventArgs>? BasicAcksAsync;

        public event AsyncEventHandler<BasicNackEventArgs>? BasicNacksAsync;

        public event AsyncEventHandler<BasicReturnEventArgs>? BasicReturnAsync;

        public event AsyncEventHandler<CallbackExceptionEventArgs>? CallbackExceptionAsync;

        public event AsyncEventHandler<FlowControlEventArgs>? FlowControlAsync;





        public event AsyncEventHandler<ShutdownEventArgs>? ChannelShutdownAsync;
#pragma warning restore CS0067


        public ValueTask<ulong> GetNextPublishSequenceNumberAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult(NextTag());

        public ValueTask BasicAckAsync(ulong deliveryTag, bool multiple, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;

        public ValueTask BasicNackAsync(ulong deliveryTag, bool multiple, bool requeue, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;

        public ValueTask BasicRejectAsync(ulong deliveryTag, bool requeue, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;

        public Task BasicCancelAsync(string consumerTag, bool noWait = default, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<string> BasicConsumeAsync(string queue, bool autoAck, string consumerTag, bool noLocal, bool exclusive, IDictionary<string, object?>? arguments, IAsyncBasicConsumer consumer, CancellationToken cancellationToken = default)
            => Task.FromResult(default(string)!);

        public Task<BasicGetResult?> BasicGetAsync(string queue, bool autoAck, CancellationToken cancellationToken = default)
            => Task.FromResult<BasicGetResult?>(null);

        public Task BasicQosAsync(uint prefetchSize, ushort prefetchCount, bool global, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task CloseAsync(ushort repCode, string repText, bool abort = default, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task CloseAsync(ShutdownEventArgs closeReason, bool abort)
            => Task.CompletedTask;

        public Task CloseAsync(ShutdownEventArgs closeReason, bool abort, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task ExchangeDeclareAsync(string exchange, string type, bool durable, bool autoDelete, IDictionary<string, object?>? arguments, bool nowait = default, bool isInternal = default, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task ExchangeDeclarePassiveAsync(string exchange, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task ExchangeDeleteAsync(string exchange, bool ifUnused = default, bool nowait = default, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task ExchangeBindAsync(string destination, string source, string routingKey, IDictionary<string, object?>? arguments, bool nowait = default, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task ExchangeUnbindAsync(string destination, string source, string routingKey, IDictionary<string, object?>? arguments, bool nowait = default, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<QueueDeclareOk> QueueDeclareAsync(string queue, bool durable, bool exclusive, bool autoDelete, IDictionary<string, object?>? arguments, bool nowait = default, bool isExclusive = default, CancellationToken cancellationToken = default)
            => Task.FromResult(default(QueueDeclareOk)!);

        public Task<QueueDeclareOk> QueueDeclarePassiveAsync(string queue, CancellationToken cancellationToken = default)
            => Task.FromResult(default(QueueDeclareOk)!);

        public Task<uint> QueueDeleteAsync(string queue, bool ifUnused = default, bool ifEmpty = default, bool nowait = default, CancellationToken cancellationToken = default)
            => Task.FromResult(default(uint));

        public Task<uint> QueuePurgeAsync(string queue, CancellationToken cancellationToken = default)
            => Task.FromResult(default(uint));

        public Task QueueBindAsync(string queue, string exchange, string routingKey, IDictionary<string, object?>? arguments, bool nowait = default, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task QueueUnbindAsync(string queue, string exchange, string routingKey, IDictionary<string, object?>? arguments, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<uint> MessageCountAsync(string queue, CancellationToken cancellationToken = default)
            => Task.FromResult(default(uint));

        public Task<uint> ConsumerCountAsync(string queue, CancellationToken cancellationToken = default)
            => Task.FromResult(default(uint));

        public Task TxCommitAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task TxRollbackAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task TxSelectAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        ValueTask IChannel.BasicPublishAsync<TProperties>(string exchange, string routingKey, bool mandatory, TProperties properties, ReadOnlyMemory<byte> body, CancellationToken cancellationToken)
            => ValueTask.CompletedTask;

        ValueTask IChannel.BasicPublishAsync<TProperties>(CachedString exchange, CachedString routingKey, bool mandatory, TProperties properties, ReadOnlyMemory<byte> body, CancellationToken cancellationToken)
            => ValueTask.CompletedTask;

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync()
            => ValueTask.CompletedTask;

        public void FireAck(ulong deliveryTag, bool multiple)
            => BasicAcksAsync?.Invoke(this, new BasicAckEventArgs(deliveryTag, multiple, default));

        private ulong NextTag()
        {
            var tag = Interlocked.Increment(ref _tagCounter);
            lock (PublishedTags)
                PublishedTags.Add(tag);
            return tag;
        }
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++)
            await Task.Delay(10);

        Assert.True(condition(), "condition was never met");
    }

    [Fact]
    public async Task MultiAckCoveringOnlyEarlierTags_DoesNotConfirmLaterPublish()
    {
        var channel = new FakeChannel(TimeSpan.FromMilliseconds(150));
        var outbox = new RabbitOutboxChannel(channel);

        var earlier = outbox.PublishAsync("ex", "rk", Encoding.UTF8.GetBytes("a"), null);
        var later = outbox.PublishAsync("ex", "rk", Encoding.UTF8.GetBytes("b"), null);
        await WaitUntil(() => channel.PublishedTags.Count == 2);

        // Broker multi-acks the earlier publish only; the in-flight later
        // publish's tag is not covered by this frame.
        channel.FireAck(channel.PublishedTags[0], multiple: true);

        var earlierOutcome = await earlier;
        var laterOutcome = await later;

        Assert.True(earlierOutcome.IsConfirmed);
        Assert.False(laterOutcome.IsConfirmed);
    }

    [Fact]
    public async Task SingleAckForOwnTag_ConfirmsPublish()
    {
        var channel = new FakeChannel(TimeSpan.FromSeconds(5));
        var outbox = new RabbitOutboxChannel(channel);

        var publish = outbox.PublishAsync("ex", "rk", Encoding.UTF8.GetBytes("a"), null);
        await WaitUntil(() => channel.PublishedTags.Count == 1);

        channel.FireAck(channel.PublishedTags[0], multiple: false);

        var outcome = await publish;

        Assert.True(outcome.IsConfirmed);
        Assert.Equal((long)channel.PublishedTags[0], outcome.PublishId);
    }
}
