namespace RaspiStorage.Exceptions;

public abstract class ApiException(string message, int statusCode, string errorCode) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string ErrorCode { get; } = errorCode;
}

public sealed class NotFoundException(string message) : ApiException(message, StatusCodes.Status404NotFound, "not_found");

public sealed class ConflictException(string message) : ApiException(message, StatusCodes.Status409Conflict, "conflict");

public sealed class InvalidNameException(string message) : ApiException(message, StatusCodes.Status400BadRequest, "invalid_name");

public sealed class QuotaExceededException(string message) : ApiException(message, StatusCodes.Status507InsufficientStorage, "quota_exceeded");