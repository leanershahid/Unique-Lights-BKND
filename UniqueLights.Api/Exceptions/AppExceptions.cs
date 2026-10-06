namespace UniqueLights.Api.Exceptions;

/// <summary>Base type for expected, client-facing errors. The message is returned to the client verbatim.</summary>
public abstract class AppException(string message, int statusCode) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

public sealed class NotFoundException(string message = "The requested resource was not found.")
    : AppException(message, StatusCodes.Status404NotFound);

public sealed class ConflictException(string message) : AppException(message, StatusCodes.Status409Conflict);

public sealed class BusinessRuleException(string message) : AppException(message, StatusCodes.Status422UnprocessableEntity);

public sealed class ForbiddenException(string message = "You do not have permission to perform this action.")
    : AppException(message, StatusCodes.Status403Forbidden);

public sealed class UnauthorizedException(string message = "Authentication is required.")
    : AppException(message, StatusCodes.Status401Unauthorized);

public sealed class ServiceUnavailableException(string message) : AppException(message, StatusCodes.Status503ServiceUnavailable);

/// <summary>400 with a field -> messages dictionary (keys camelCase).</summary>
public sealed class ValidationException : AppException
{
    public ValidationException(IReadOnlyDictionary<string, string[]> errors, string message = "Validation failed")
        : base(message, StatusCodes.Status400BadRequest)
    {
        Errors = new Dictionary<string, string[]>(errors);
    }

    public ValidationException(string field, string error)
        : this(new Dictionary<string, string[]> { [field] = [error] })
    {
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}
