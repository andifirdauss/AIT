namespace AIThread.Service.Models;

// Invalid user input; the API turns it into a 400 with the message shown in the UI.
public class AppValidationException(string message) : Exception(message);

// A business page was saved by someone else (another developer, or the wiki writer) since it was loaded.
public class BusinessPageConflictException(string message) : AppValidationException(message);

// Failure talking to 9router (HTTP error, timeout, malformed stream).
public class NineRouterException(string message, int? statusCode = null, Exception? innerException = null, TimeSpan? retryAfter = null)
    : Exception(message, innerException)
{
    public int? StatusCode { get; } = statusCode;

    // Wait requested by the upstream (Retry-After header), when it sent one.
    public TimeSpan? RetryAfter { get; } = retryAfter;

    // 9router was reached, but the provider account behind the model is logged out (expired OAuth token, revoked
    // key). Retrying does not help until someone logs in again; another model (a fallback node) may.
    public bool IsProviderLoginRequired { get; init; }

    public bool IsTransient => !IsProviderLoginRequired && StatusCode is null or 408 or 429 or >= 500;

    public bool IsRateLimited => StatusCode == 429;

    // The router answered but this model is unavailable right now; another model may still work.
    public bool IsModelUnavailable => IsProviderLoginRequired || StatusCode is 408 or 429 or >= 500;
}

// Failure calling the Telegram Bot API. ErrorCode is Telegram's error_code (or the HTTP status); null when
// Telegram could not be reached at all.
public class TelegramApiException(string message, int? errorCode = null, TimeSpan? retryAfter = null, Exception? innerException = null)
    : Exception(message, innerException)
{
    public int? ErrorCode { get; } = errorCode;

    // Flood control: seconds Telegram asked to wait before the next call.
    public TimeSpan? RetryAfter { get; } = retryAfter;

    // 401 for a revoked or wrong token, 404 for a token that is not even well-formed.
    public bool IsUnauthorized => ErrorCode is 401 or 404;

    // Another client is polling with the same token.
    public bool IsConflict => ErrorCode == 409;

    public bool IsParseError => ErrorCode == 400 && Message.Contains("can't parse entities", StringComparison.OrdinalIgnoreCase);

    public bool IsNotModified => ErrorCode == 400 && Message.Contains("message is not modified", StringComparison.OrdinalIgnoreCase);

    public bool IsMessageGone => ErrorCode == 400 &&
                                 (Message.Contains("message to edit not found", StringComparison.OrdinalIgnoreCase) ||
                                  Message.Contains("message can't be edited", StringComparison.OrdinalIgnoreCase));

    // The user blocked the bot or the chat no longer exists: nothing more can be delivered.
    public bool IsChatUnreachable => ErrorCode == 403 ||
                                     (ErrorCode == 400 && Message.Contains("chat not found", StringComparison.OrdinalIgnoreCase));
}

// The pipeline cannot continue (missing nodes, planner gave up, ...). Message is user-facing.
public class OrchestrationException(string message, Exception? innerException = null) : Exception(message, innerException);
