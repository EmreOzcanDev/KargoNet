using Microsoft.AspNetCore.Mvc;
using Portal.Web.Models;
using Portal.Web.Services;

namespace Portal.Web.Controllers;

public class ShipmentsController : Controller
{
    private const string ErrorBanner = "We couldn't reach the KargoNet service. Check your connection and try again.";

    private readonly IPortalApiClient _api;

    public ShipmentsController(IPortalApiClient api)
    {
        _api = api;
    }

    [HttpGet]
    public IActionResult New()
    {
        return View(new NewShipmentViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> New(NewShipmentViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return View(model);
        }

        try
        {
            var dto = await _api.CreateShipmentAsync(
                new CreateShipmentRequestDto(model.TrackingNumber, model.Description, model.Destination),
                cancellationToken);

            return Redirect($"/track?number={Uri.EscapeDataString(dto.trackingNumber)}");
        }
        catch (ApiRequestException)
        {
            model.ErrorMessage = ErrorBanner;
            return View(model);
        }
    }
}
