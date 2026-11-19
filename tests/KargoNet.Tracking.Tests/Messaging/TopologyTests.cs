using KargoNet.Messaging.Topology;

namespace KargoNet.Tracking.Tests.Messaging;

public class TopologyTests
{
    [Fact]
    public void Exchange_IsTopicNamedKargonetShipments()
    {
        var spec = new TopologySpec();

        Assert.Equal("kargonet.shipments", spec.Exchange.Name);
        Assert.Equal("topic", spec.Exchange.Type);
    }

    [Fact]
    public void MainQueue_IsBoundWithShipmentKey()
    {
        var spec = new TopologySpec();

        Assert.Equal("shipments.main", spec.MainQueue.Name);
        Assert.Equal("shipment", spec.MainQueue.RoutingKey);
        Assert.Null(spec.MainQueue.Arguments);
    }

    [Fact]
    public void RetryQueue_HasDeadLetterArguments()
    {
        var spec = new TopologySpec();
        var arguments = spec.RetryQueue.Arguments!;

        Assert.Equal("shipments.retry", spec.RetryQueue.Name);
        Assert.Equal("shipment.retry", spec.RetryQueue.RoutingKey);
        Assert.Equal(5000, arguments["x-message-ttl"]);
        Assert.Equal("kargonet.shipments", arguments["x-dead-letter-exchange"]);
        Assert.Equal("shipment", arguments["x-dead-letter-routing-key"]);
    }

    [Fact]
    public void Dlq_IsBoundWithShipmentDeadKey()
    {
        var spec = new TopologySpec();

        Assert.Equal("shipments.dlq", spec.Dlq.Name);
        Assert.Equal("shipment.dead", spec.Dlq.RoutingKey);
        Assert.Null(spec.Dlq.Arguments);
    }

    [Fact]
    public void RabbitTopology_ConstantsMatchSpec()
    {
        var spec = new TopologySpec();

        Assert.Equal(RabbitTopology.Exchange, spec.Exchange.Name);
        Assert.Equal(RabbitTopology.ExchangeType, spec.Exchange.Type);
        Assert.Equal(RabbitTopology.MainQueue, spec.MainQueue.Name);
        Assert.Equal(RabbitTopology.RetryQueue, spec.RetryQueue.Name);
        Assert.Equal(RabbitTopology.Dlq, spec.Dlq.Name);
        Assert.Equal(RabbitTopology.ShipmentRoutingKey, spec.MainQueue.RoutingKey);
        Assert.Equal(RabbitTopology.RetryRoutingKey, spec.RetryQueue.RoutingKey);
        Assert.Equal(RabbitTopology.DlqRoutingKey, spec.Dlq.RoutingKey);
    }
}
