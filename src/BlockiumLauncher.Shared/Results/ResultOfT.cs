using BlockiumLauncher.Shared.Errors;

namespace BlockiumLauncher.Shared.Results;

public readonly struct Result<T>
{
    private readonly T? ValueField;

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public Error Error { get; }

    public T Value => IsSuccess
        ? ValueField!
        : throw new InvalidOperationException("Cannot access the value of a failed result.");

    private Result(T Value)
    {
        IsSuccess = true;
        Error = global::BlockiumLauncher.Shared.Errors.Error.None;
        ValueField = Value;
    }

    private Result(Error Error)
    {
        if (Error == global::BlockiumLauncher.Shared.Errors.Error.None)
        {
            throw new ArgumentException("Failure result cannot use Error.None.", nameof(Error));
        }

        IsSuccess = false;
        this.Error = Error;
        ValueField = default;
    }

    public static Result<T> Success(T Value)
    {
        return new(Value);
    }

    public static Result<T> Failure(Error Error)
    {
        return new(Error);
    }
}
