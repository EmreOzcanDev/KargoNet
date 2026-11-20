using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Portal.Web.Models;
using Portal.Web.Services;

namespace Portal.Web.Controllers;

public class TrackController : Controller
{
    private const string ServiceErrorCopy =
        "We couldn't reach the KargoNet service. Check your connection and try again.";

    private readonly IPortalApiClient _api;

    public TrackController(IPortalApiClient api)
    {
        _api = api;
    }

    public async Task<IActionResult> Index(string? number, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(number))
        {
            return View(new TrackingViewModel { State = TrackState.Empty });
        }

        TrackingDto? tracking;
        try
        {
            tracking = await _api.GetTrackingAsync(number, ct);
        }
        catch (ApiRequestException)
        {
            return View(new TrackingViewModel
            {
                State = TrackState.Error,
                TrackingNumber = number,
                ErrorMessage = ServiceErrorCopy
            });
        }

        if (tracking is null)
        {
            return View(new TrackingViewModel
            {
                State = TrackState.NotFound,
                TrackingNumber = number
            });
        }

        var model = new TrackingViewModel
        {
            State = TrackState.Found,
            TrackingNumber = tracking.trackingNumber,
            Description = tracking.description,
            Destination = tracking.destination,
            Status = tracking.status,
            Created = tracking.createdAt.ToString("yyyy-MM-dd HH:mm"),
            Updated = tracking.updatedAt.ToString("yyyy-MM-dd HH:mm")
        };

        try
        {
            var events = await _api.GetTimelineAsync(number, ct);
            model.Timeline = BuildTimeline(events);
        }
        catch (ApiRequestException)
        {
            model.TimelineError = ServiceErrorCopy;
        }

        return View(model);
    }

    private static List<TimelineItem> BuildTimeline(IReadOnlyList<TrackingEventDto> events)
    {
        var items = new List<TimelineItem>();

        foreach (var evt in events.OrderBy(e => e.occurredAt))
        {
            var item = new TimelineItem
            {
                OccurredAt = evt.occurredAt.ToString("yyyy-MM-dd HH:mm"),
                Label = evt.eventType
            };

            try
            {
                using var doc = JsonDocument.Parse(evt.payloadJson);
                var root = doc.RootElement;

                switch (evt.eventType)
                {
                    case "shipment.created":
                        item.Label = "Shipment created";
                        var parts = new List<string>();
                        AddStringPart(root, "description", parts);
                        AddStringPart(root, "destination", parts);
                        if (parts.Count > 0)
                        {
                            item.SubLine = string.Join(" / ", parts);
                        }
                        break;

                    case "shipment.status.changed":
                        if (root.TryGetProperty("status", out var status) &&
                            status.ValueKind == JsonValueKind.String)
                        {
                            item.Label = $"Status changed to {status.GetString()}";
                            item.Status = status.GetString();
                        }
                        break;

                    default:
                        // Unknown event type: raw text as label.
                        break;
                }
            }
            catch (JsonException)
            {
                // Unparseable payload: event type as label, no sub-line.
            }

            items.Add(item);
        }

        return items;
    }

    private static void AddStringPart(JsonElement root, string name, List<string> parts)
    {
        if (root.TryGetProperty(name, out var value) &&
            value.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(value.GetString()))
        {
            parts.Add(value.GetString()!);
        }
    }
}
