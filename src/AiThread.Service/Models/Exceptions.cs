namespace AiThread.Service.Models;

// Invalid user input; the API turns it into a 400 with the message shown in the UI.
public class AppValidationException(string message) : Exception(message);

// Failure talking to 9router (HTTP error, timeout, malformed stream).
public class NineRouterException(string message, int? statusCode = null, Exception? innerException = null)
    : Exception(message, innerException)
{
    public int? StatusCode { get; } = statusCode;

    public bool IsTransient => StatusCode is null or 408 or 429 or >= 500;
}

// The pipeline cannot continue (missing nodes, planner gave up, ...). Message is user-facing.
public class OrchestrationException(string message) : Exception(message);
