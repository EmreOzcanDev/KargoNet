using System.ComponentModel.DataAnnotations;

namespace Portal.Web.Models;

public class NewShipmentViewModel
{
    public string? TrackingNumber { get; set; }

    public string? Description { get; set; }

    [Required]
    [Display(Name = "Destination")]
    public string Destination { get; set; } = string.Empty;

    public string? ErrorMessage { get; set; }
}
