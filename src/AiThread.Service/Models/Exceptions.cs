namespace AiThread.Service.Models;

// Invalid user input; the API turns it into a 400 with the message shown in the UI.
public class AppValidationException(string message) : Exception(message);

// Failure talking to 9router (HTTP error, timeout, malformed stream).
public class NineRouterException(string message, int? statusCode = null, Exception? innerException = null, TimeSpan? retryAfter = null)
    : Exception(message, innerException)
{
    public int? StatusCode { get; } = statusCode;

    // Wait requested by the upstream (Retry-After header), when it sent one.
    public TimeSpan? RetryAfter { get; } = retryAfter;

    public bool IsTransient => StatusCode is null or 408 or 429 or >= 500;

    public bool IsRateLimited => StatusCode == 429;

    // The router answered but this model is unavailable right now; another model may still work.
    public bool IsModelUnavailable => StatusCode is 408 or 429 or >= 500;
}

// The pipeline cannot continue (missing nodes, planner gave up, ...). Message is user-facing.
public class OrchestrationException(string message, Exception? innerException = null) : Exception(message, innerException);
