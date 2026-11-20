namespace Portal.Web.Models;

public class TimelineItem
{
    public string Label { get; set; } = string.Empty;

    /// <summary>Description/destination sub-line for created events.</summary>
    public string? SubLine { get; set; }

    /// <summary>Status to badge (status-changed events).</summary>
    public string? Status { get; set; }

    /// <summary>Pre-formatted yyyy-MM-dd HH:mm.</summary>
    public string OccurredAt { get; set; } = string.Empty;
}
