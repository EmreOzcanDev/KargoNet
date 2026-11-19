using System.Text.Json;
using System.Text.Json.Serialization;

namespace KargoNet.Contracts.Events;

/// <summary>
/// camelCase System.Text.Json (de)serialization for <see cref="EventEnvelope{TPayload}"/>.
/// The payload is projected in and out of the envelope's <c>payload</c> element
/// separately from the envelope fields.
/// </summary>
public static class EventSerializer
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    /// <summary>Serializes the envelope to UTF-8 JSON bytes.</summary>
    public static byte[] Serialize<TPayload>(EventEnvelope<TPayload> envelope)
        => JsonSerializer.SerializeToUtf8Bytes(envelope, Options);

    /// <summary>Parses UTF-8 JSON bytes back into a typed envelope, reconstructing the payload from the <c>payload</c> element.</summary>
    public static EventEnvelope<TPayload> Deserialize<TPayload>(ReadOnlySpan<byte> utf8)
    {
        using var document = JsonDocument.Parse(utf8.ToArray());
        var root = document.RootElement;

        var eventId = root.GetProperty("eventId").GetGuid();
        var eventType = root.GetProperty("eventType").GetString()!;
        var occurredAt = root.GetProperty("occurredAt").GetDateTimeOffset();

        // Re-project the typed payload into a standalone element: elements read
        // off the parsed document are backed by it and become unusable once it
        // is disposed on return.
        var payload = root.GetProperty("payload").Deserialize<TPayload>(Options)!;
        var payloadElement = JsonSerializer.SerializeToElement(payload, Options);

        return new EventEnvelope<TPayload>(eventId, eventType, occurredAt, payloadElement);
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        // Enum values (e.g. ShipmentStatus) are serialized by their exact name,
        // never as a bare integer.
        options.Converters.Add(new JsonStringEnumConverter());

        return options;
    }
}
