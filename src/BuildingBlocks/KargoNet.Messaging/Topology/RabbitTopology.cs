using RabbitMQ.Client;

namespace KargoNet.Messaging.Topology;

/// <summary>
/// Well-known topology names and the declarer that applies <see cref="TopologySpec"/>
/// to a live channel. The live declaration is exercised in Phase 6 against a real broker.
/// </summary>
public static class RabbitTopology
{
    public const string Exchange = "kargonet.shipments";
    public const string MainQueue = "shipments.main";
    public const string RetryQueue = "shipments.retry";
    public const string Dlq = "shipments.dlq";

    public const string ShipmentRoutingKey = "shipment";
    public const string RetryRoutingKey = "shipment.retry";
    public const string DlqRoutingKey = "shipment.dead";

    public const string ExchangeType = "topic";

    private static readonly TopologySpec Spec = new();

    /// <summary>Applies the full topology (exchange, three queues, three bindings) to <paramref name="channel"/>.</summary>
    public static Task DeclareAsync(IChannel channel)
    {
        var retryArguments = new Dictionary<string, object?>();
        foreach (var argument in Spec.RetryQueue.Arguments!)
            retryArguments[argument.Key] = argument.Value;

        return Task.WhenAll(
            channel.ExchangeDeclareAsync(
                Spec.Exchange.Name,
                Spec.Exchange.Type,
                durable: true,
                autoDelete: false,
                arguments: null),
            channel.QueueDeclareAsync(Spec.MainQueue.Name, durable: true, exclusive: false, autoDelete: false, arguments: null),
            channel.QueueDeclareAsync(Spec.RetryQueue.Name, durable: true, exclusive: false, autoDelete: false, arguments: retryArguments),
            channel.QueueDeclareAsync(Spec.Dlq.Name, durable: true, exclusive: false, autoDelete: false, arguments: null),
            channel.QueueBindAsync(Spec.MainQueue.Name, Spec.Exchange.Name, Spec.MainQueue.RoutingKey, arguments: null),
            channel.QueueBindAsync(Spec.RetryQueue.Name, Spec.Exchange.Name, Spec.RetryQueue.RoutingKey, arguments: null),
            channel.QueueBindAsync(Spec.Dlq.Name, Spec.Exchange.Name, Spec.Dlq.RoutingKey, arguments: null));
    }
}
