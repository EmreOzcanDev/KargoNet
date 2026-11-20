namespace Portal.Web.Models;

public enum TrackState
{
    Found,
    NotFound,
    Empty,
    Error
}

public class TrackingViewModel
{
    public TrackState State { get; set; }

    public string? TrackingNumber { get; set; }

    public string? Description { get; set; }

    public string Destination { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string Created { get; set; } = string.Empty;

    public string Updated { get; set; } = string.Empty;

    public List<TimelineItem> Timeline { get; set; } = [];

    public string? TimelineError { get; set; }

    public string? ErrorMessage { get; set; }
}
