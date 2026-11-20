namespace Portal.Web.Services;

/// <summary>
/// Thrown when an upstream API call fails (5xx response or timeout).
/// </summary>
public class ApiRequestException : Exception
{
    public ApiRequestException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}
