using System.Text;
using System.Text.Json;
using KargoNet.Contracts.Events;
using KargoNet.Contracts.Models;
using KargoNet.Messaging.Consuming;
using KargoNet.Messaging.Topology;

namespace KargoNet.Tracking.Tests.Messaging;

public class ConsumerBaseTests
{
    private sealed class RecordingBroker : IConsumerBroker
    {
        public List<ulong> Acks { get; } = [];


        public record Publish(string Exchange, string RoutingKey, byte[] Body, IDictionary<string, object?>? Headers);


        public List<Publish> Publishes { get; } = [];

        public Exception? PublishFailure;





        public Task AckAsync(ulong deliveryTag)
        {
            Acks.Add(deliveryTag);
            return Task.CompletedTask;
        }

        public Task PublishAsync(string exchange, string routingKey, ReadOnlyMemory<byte> body, IDictionary<string, object?>? headers)
        {
            if (PublishFailure is not null)
                throw PublishFailure;

            Publishes.Add(new Publish(exchange, routingKey, body.ToArray(), headers));
            return Task.CompletedTask;
        }
    }

    private sealed class TrackingConsumer : ConsumerBase<ShipmentCreatedPayload>
    {
        public TrackingConsumer(IConsumerBroker broker, Func<EventEnvelope<ShipmentCreatedPayload>, Task> handler)
            : base(broker, handler)
        {
        }
    }

    private static EventEnvelope<ShipmentCreatedPayload> Envelope() =>
        new(Guid.NewGuid(), "ShipmentCreated", DateTimeOffset.UtcNow,
            JsonSerializer.SerializeToElement(new ShipmentCreatedPayload("TRK-001", "Box", "Istanbul")));

    private static byte[] Body(EventEnvelope<ShipmentCreatedPayload> envelope) =>
        EventSerializer.Serialize(envelope);

    [Fact]
    public async Task SuccessfulDelivery_AcksAndDoesNotRepublish()
    {
        var broker = new RecordingBroker();
        var consumer = new TrackingConsumer(broker, _ => Task.CompletedTask);
        var body = Body(Envelope());

        await consumer.HandleDeliveryAsync(11, body, null);

        Assert.Equal([11UL], broker.Acks);
        Assert.Empty(broker.Publishes);
    }

    [Fact]
    public async Task FirstFailure_RepublishesToRetryWithBumpedCountAndExceptionHeader()
    {
        var broker = new RecordingBroker();
        var consumer = new TrackingConsumer(broker, _ => throw new InvalidOperationException("handler exploded"));
        var originalHeaders = new Dictionary<string, object?> { ["correlation"] = "abc" };
        var body = Body(Envelope());

        await consumer.HandleDeliveryAsync(21, body, originalHeaders);

        var publish = Assert.Single(broker.Publishes);
        Assert.Equal(RabbitTopology.Exchange, publish.Exchange);
        Assert.Equal(RabbitTopology.RetryRoutingKey, publish.RoutingKey);
        Assert.Equal(body, publish.Body);
        Assert.Equal(1, publish.Headers!["x-retry-count"]);
        Assert.Equal("handler exploded", publish.Headers!["x-exception"]);
        Assert.Equal("abc", publish.Headers!["correlation"]);
        Assert.Equal([21UL], broker.Acks);
    }

    [Fact]
    public async Task SecondFailure_RepublishesWithRetryCountTwo()
    {
        var broker = new RecordingBroker();
        var consumer = new TrackingConsumer(broker, _ => throw new InvalidOperationException("handler exploded"));
        var headers = new Dictionary<string, object?> { ["x-retry-count"] = 1 };
        var body = Body(Envelope());

        await consumer.HandleDeliveryAsync(31, body, headers);

        var publish = Assert.Single(broker.Publishes);
        Assert.Equal(RabbitTopology.RetryRoutingKey, publish.RoutingKey);
        Assert.Equal(2, publish.Headers!["x-retry-count"]);
        Assert.Equal([31UL], broker.Acks);
    }

    [Fact]
    public async Task ThirdFailure_RoutesToDlqWithoutRetryRepublish()
    {
        var broker = new RecordingBroker();
        var consumer = new TrackingConsumer(broker, _ => throw new InvalidOperationException("handler exploded"));
        var headers = new Dictionary<string, object?> { ["x-retry-count"] = 2 };
        var body = Body(Envelope());

        await consumer.HandleDeliveryAsync(41, body, headers);

        var publish = Assert.Single(broker.Publishes);
        Assert.Equal(RabbitTopology.Exchange, publish.Exchange);
        Assert.Equal(RabbitTopology.DlqRoutingKey, publish.RoutingKey);
        Assert.Equal(body, publish.Body);
        Assert.Equal(2, publish.Headers!["x-retry-count"]);
        Assert.Equal("handler exploded", publish.Headers!["x-exception"]);
        Assert.Equal([41UL], broker.Acks);
    }


    [Fact]
    public async Task MalformedBody_RoutesToDlqAndAcks()
    {
        var broker = new RecordingBroker();
        var consumer = new TrackingConsumer(broker, _ => Task.CompletedTask);
        var corrupt = Encoding.UTF8.GetBytes("{not valid json");

        await consumer.HandleDeliveryAsync(51, corrupt, null);

        var publish = Assert.Single(broker.Publishes);
        Assert.Equal(RabbitTopology.Exchange, publish.Exchange);
        Assert.Equal(RabbitTopology.DlqRoutingKey, publish.RoutingKey);
        Assert.Equal(corrupt, publish.Body);
        Assert.Equal([51UL], broker.Acks);
    }

    [Fact]
    public async Task UnparseableRetryCountHeader_TreatedAsFirstAttempt()
    {
        var broker = new RecordingBroker();
        var consumer = new TrackingConsumer(broker, _ => throw new InvalidOperationException("handler exploded"));
        var headers = new Dictionary<string, object?> { ["x-retry-count"] = "not-a-number" };
        var body = Body(Envelope());

        await consumer.HandleDeliveryAsync(61, body, headers);

        var publish = Assert.Single(broker.Publishes);
        Assert.Equal(RabbitTopology.RetryRoutingKey, publish.RoutingKey);
        Assert.Equal(1, publish.Headers!["x-retry-count"]);
        Assert.Equal([61UL], broker.Acks);
    }

    [Fact]
    public async Task RepublishFailure_OriginalStillAcksAndNoThrow()
    {
        var broker = new RecordingBroker
        {
            PublishFailure = new InvalidOperationException("broker down"),
        };
        var consumer = new TrackingConsumer(broker, _ => throw new InvalidOperationException("handler exploded"));
        var body = Body(Envelope());

        await consumer.HandleDeliveryAsync(71, body, null);

        Assert.Equal([71UL], broker.Acks);
    }
}
