namespace KargoNet.Messaging.Publishing;

/// <summary>
/// Outcome of a broker publish, decoupled from RabbitMQ's <c>PublishResult</c>
/// so the confirm-handling logic is unit-testable with a fake.
/// </summary>
public readonly record struct PublishOutcome(bool IsConfirmed, long PublishId);
