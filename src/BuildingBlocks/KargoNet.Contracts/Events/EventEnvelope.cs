using System.Text.Json;

namespace KargoNet.Contracts.Events;

/// <summary>
/// Cross-service wire wrapper for shipment events. Serialized into RabbitMQ
/// and persisted in the DLQ; Phases 3-4 deserialize it.
/// </summary>
public record EventEnvelope<TPayload>(
    Guid EventId,
    string EventType,
    DateTimeOffset OccurredAt,
    JsonElement? Payload);
