using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace Portal.Web.Services;

public class PortalApiClient : IPortalApiClient
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly Uri _shipmentApiUrl;
    private readonly Uri _trackingApiUrl;
    private readonly HttpClient _http;

    public PortalApiClient(Uri shipmentApiUrl, Uri trackingApiUrl)
    {
        _shipmentApiUrl = shipmentApiUrl;
        _trackingApiUrl = trackingApiUrl;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
    }

    public async Task<TrackingDto?> GetTrackingAsync(string number, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.GetAsync(
                new Uri(_trackingApiUrl, $"api/tracking/{Uri.EscapeDataString(number)}"), ct);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
            EnsureSuccess(response);
            return await response.Content.ReadFromJsonAsync<TrackingDto>(Json, ct);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new ApiRequestException("Timed out reaching the tracking service.", ex);
        }
    }

    public async Task<IReadOnlyList<TrackingEventDto>> GetTimelineAsync(string number, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.GetAsync(
                new Uri(_trackingApiUrl, $"api/tracking/{Uri.EscapeDataString(number)}/events"), ct);
            EnsureSuccess(response);
            return await response.Content.ReadFromJsonAsync<List<TrackingEventDto>>(Json, ct)
                   ?? new List<TrackingEventDto>();
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new ApiRequestException("Timed out reaching the tracking service.", ex);
        }
    }

    public async Task<ShipmentDto> CreateShipmentAsync(CreateShipmentRequestDto request, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.PostAsJsonAsync(
                new Uri(_shipmentApiUrl, "api/shipments"), request, Json, ct);
            EnsureSuccess(response);
            var dto = await response.Content.ReadFromJsonAsync<ShipmentDto>(Json, ct);
            if (dto is null)
            {
                throw new ApiRequestException("Shipment service returned an empty response.");
            }
            return dto;
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new ApiRequestException("Timed out reaching the shipment service.", ex);
        }
    }

    private static void EnsureSuccess(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new ApiRequestException(
                $"Upstream request failed with status {(int)response.StatusCode}.");
        }
    }
}
