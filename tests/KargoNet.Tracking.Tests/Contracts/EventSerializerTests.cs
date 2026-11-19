using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using KargoNet.Contracts.Events;
using KargoNet.Contracts.Models;

namespace KargoNet.Tracking.Tests.Contracts;

public class EventSerializerTests
{
    private static readonly JsonSerializerOptions ElementOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    static EventSerializerTests()
    {
        ElementOptions.Converters.Add(new JsonStringEnumConverter());
    }

    [Fact]
    public void RoundTrip_StatusChanged_PreservesEveryField()
    {
        var expectedPayload = new ShipmentStatusChangedPayload(
            Guid.NewGuid(),
            "TRK-001",
            ShipmentStatus.Created,
            ShipmentStatus.InTransit);

        var original = new EventEnvelope<ShipmentStatusChangedPayload>(
            Guid.NewGuid(),
            "ShipmentStatusChanged",
            new DateTimeOffset(2026, 11, 19, 12, 30, 0, TimeSpan.Zero),
            JsonSerializer.SerializeToElement(expectedPayload, ElementOptions));

        var bytes = EventSerializer.Serialize(original);
        var restored = EventSerializer.Deserialize<ShipmentStatusChangedPayload>(bytes);

        Assert.Equal(original.EventId, restored.EventId);
        Assert.Equal(original.EventType, restored.EventType);
        Assert.Equal(original.OccurredAt, restored.OccurredAt);

        var payload = restored.Payload!.Value.Deserialize<ShipmentStatusChangedPayload>(ElementOptions)!;
        Assert.Equal(expectedPayload.ShipmentId, payload.ShipmentId);
        Assert.Equal(expectedPayload.TrackingNumber, payload.TrackingNumber);
        Assert.Equal(expectedPayload.FromStatus, payload.FromStatus);
        Assert.Equal(expectedPayload.ToStatus, payload.ToStatus);
    }
    [Fact]
    public void RoundTrip_Created_PreservesEveryField()
    {
        var expectedPayload = new ShipmentCreatedPayload("TRK-042", "Fragile box", "Istanbul");

        var original = new EventEnvelope<ShipmentCreatedPayload>(
            Guid.NewGuid(),
            "ShipmentCreated",
            new DateTimeOffset(2026, 11, 19, 8, 0, 0, TimeSpan.FromHours(3)),
            JsonSerializer.SerializeToElement(expectedPayload, ElementOptions));

        var bytes = EventSerializer.Serialize(original);
        var restored = EventSerializer.Deserialize<ShipmentCreatedPayload>(bytes);

        Assert.Equal(original.EventId, restored.EventId);
        Assert.Equal(original.EventType, restored.EventType);
        Assert.Equal(original.OccurredAt, restored.OccurredAt);

        var payload = restored.Payload!.Value.Deserialize<ShipmentCreatedPayload>(ElementOptions)!;
        Assert.Equal(expectedPayload.Description, payload.Description);
        Assert.Equal(expectedPayload.Destination, payload.Destination);
    }

    [Fact]
    public void WireFormat_IsCamelCase()
    {
        var envelope = new EventEnvelope<ShipmentStatusChangedPayload>(
            Guid.NewGuid(),
            "ShipmentStatusChanged",
            DateTimeOffset.UtcNow,
            JsonSerializer.SerializeToElement(new ShipmentStatusChangedPayload(
                Guid.NewGuid(),
                "TRK-001",
                ShipmentStatus.Created,
                ShipmentStatus.InTransit), ElementOptions));

        var text = Encoding.UTF8.GetString(EventSerializer.Serialize(envelope));

        Assert.Contains("\"eventId\"", text);
        Assert.Contains("\"eventType\"", text);
        Assert.Contains("\"occurredAt\"", text);
        Assert.Contains("\"payload\"", text);
        Assert.Contains("\"fromStatus\"", text);
        Assert.Contains("\"toStatus\"", text);
    }

    [Fact]
    public void Status_IsSerializedByName_NotRawInt()
    {
        var envelope = new EventEnvelope<ShipmentStatusChangedPayload>(
            Guid.NewGuid(),
            "ShipmentStatusChanged",
            DateTimeOffset.UtcNow,
            JsonSerializer.SerializeToElement(new ShipmentStatusChangedPayload(
                Guid.NewGuid(),
                "TRK-001",
                ShipmentStatus.Created,
                ShipmentStatus.InTransit), ElementOptions));

        var text = Encoding.UTF8.GetString(EventSerializer.Serialize(envelope));

        Assert.Contains("\"Created\"", text);
        Assert.Contains("\"InTransit\"", text);
    }

}
