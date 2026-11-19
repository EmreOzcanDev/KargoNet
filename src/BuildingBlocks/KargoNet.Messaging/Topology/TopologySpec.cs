namespace KargoNet.Messaging.Topology;

/// <summary>
/// Data-only description of the RabbitMQ topology. Plain data so the exact
/// names, routing keys, and queue arguments are assertable in unit tests
/// with no broker.
/// </summary>
public sealed record ExchangeSpec(string Name, string Type);

/// <summary>
/// A queue and the routing key it is bound to on the exchange.
/// </summary>
public sealed record QueueSpec(
    string Name,
    string RoutingKey,
    IReadOnlyDictionary<string, object?>? Arguments = null);

public sealed class TopologySpec
{
    public ExchangeSpec Exchange { get; } = new("kargonet.shipments", "topic");

    public QueueSpec MainQueue { get; } = new("shipments.main", "shipment");

    public QueueSpec RetryQueue { get; } = new(
        "shipments.retry",
        "shipment.retry",
        new Dictionary<string, object?>
        {
            ["x-message-ttl"] = 5000,
            ["x-dead-letter-exchange"] = "kargonet.shipments",
            ["x-dead-letter-routing-key"] = "shipment",
        });

    public QueueSpec Dlq { get; } = new("shipments.dlq", "shipment.dead");

    /// <summary>All queues in declaration order.</summary>
    public QueueSpec[] Queues => [MainQueue, RetryQueue, Dlq];
}
