namespace Zhua.Application.Common;

/// <summary>Outcome of a use case, mapped to an HTTP status by the Api (so use cases stay transport-agnostic).</summary>
public enum ResultStatus { Ok, Created, NotFound, Conflict, BadRequest }

/// <summary>A use-case result: a status + either a value (on success) or an error message.</summary>
public sealed record Result<T>(ResultStatus Status, T? Value, string? Error)
{
    public static Result<T> Ok(T value) => new(ResultStatus.Ok, value, null);
    public static Result<T> Created(T value) => new(ResultStatus.Created, value, null);
    public static Result<T> NotFound(string error) => new(ResultStatus.NotFound, default, error);
    public static Result<T> Conflict(string error) => new(ResultStatus.Conflict, default, error);
    public static Result<T> BadRequest(string error) => new(ResultStatus.BadRequest, default, error);
}
