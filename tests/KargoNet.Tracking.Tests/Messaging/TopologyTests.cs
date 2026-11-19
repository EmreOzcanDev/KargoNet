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

        Assert.Equal(spec.Exchange.Name, RabbitTopology.Exchange);
        Assert.Equal(spec.Exchange.Type, RabbitTopology.ExchangeType);
        Assert.Equal(spec.MainQueue.Name, RabbitTopology.MainQueue);
        Assert.Equal(spec.RetryQueue.Name, RabbitTopology.RetryQueue);
        Assert.Equal(spec.Dlq.Name, RabbitTopology.Dlq);
        Assert.Equal(spec.MainQueue.RoutingKey, RabbitTopology.ShipmentRoutingKey);
        Assert.Equal(spec.RetryQueue.RoutingKey, RabbitTopology.RetryRoutingKey);
        Assert.Equal(spec.Dlq.RoutingKey, RabbitTopology.DlqRoutingKey);
    }
}
