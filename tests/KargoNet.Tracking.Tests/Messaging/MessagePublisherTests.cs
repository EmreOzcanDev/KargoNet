using System.Text;
using KargoNet.Messaging.Publishing;
using KargoNet.Messaging.Topology;

namespace KargoNet.Tracking.Tests.Messaging;

public class MessagePublisherTests
{
    private sealed class FakeOutboxChannel : IOutboxChannel
    {
        public PublishOutcome Outcome { get; init; } = new(true, 1);

        public string? Exchange { get; private set; }

        public string? RoutingKey { get; private set; }

        public byte[]? Body { get; private set; }

        public IDictionary<string, object?>? Headers { get; private set; }

        public Task<PublishOutcome> PublishAsync(string exchange, string routingKey, ReadOnlyMemory<byte> body, IDictionary<string, object?>? headers)
        {
            Exchange = exchange;
            RoutingKey = routingKey;
            Body = body.ToArray();
            Headers = headers;
            return Task.FromResult(Outcome);
        }
    }

    [Fact]
    public async Task ConfirmedPublish_PassesThrough()
    {
        var channel = new FakeOutboxChannel { Outcome = new PublishOutcome(true, 42) };
        var publisher = new MessagePublisher(channel);
        var body = "payload";
        var headers = new Dictionary<string, object?> { ["k"] = "v" };

        await publisher.PublishAsync("shipment", Encoding.UTF8.GetBytes(body), headers);

        Assert.Equal(RabbitTopology.Exchange, channel.Exchange);
        Assert.Equal("shipment", channel.RoutingKey);
        Assert.Equal(body, Encoding.UTF8.GetString(channel.Body!));
        Assert.NotNull(channel.Headers);
        Assert.Equal("v", channel.Headers!["k"]);
    }

    [Fact]
    public async Task UnconfirmedPublish_Throws()
    {
        var channel = new FakeOutboxChannel { Outcome = new PublishOutcome(false, 7) };
        var publisher = new MessagePublisher(channel);

        await Assert.ThrowsAsync<PublishNotConfirmedException>(
            () => publisher.PublishAsync("shipment", Encoding.UTF8.GetBytes("payload"), null));

        Assert.Equal(RabbitTopology.Exchange, channel.Exchange);
        Assert.Equal("shipment", channel.RoutingKey);
    }

    [Fact]
    public async Task Publish_AlwaysTargetsTopicExchange()
    {
        var channel = new FakeOutboxChannel();
        var publisher = new MessagePublisher(channel);

        await publisher.PublishAsync("shipment.retry", Encoding.UTF8.GetBytes("x"), null);

        Assert.Equal("kargonet.shipments", channel.Exchange);
    }
}
