using BlockiumLauncher.Shared.Errors;

namespace BlockiumLauncher.Shared.Results;

public readonly struct Result
{
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public Error Error { get; }

    private Result(bool IsSuccess, Error Error)
    {
        var IsValid = IsSuccess
            ? Error == global::BlockiumLauncher.Shared.Errors.Error.None
            : Error != global::BlockiumLauncher.Shared.Errors.Error.None;

        if (!IsValid)
        {
            throw new ArgumentException("Invalid error state for result.", nameof(Error));
        }

        this.IsSuccess = IsSuccess;
        this.Error = Error;
    }

    public static Result Success()
    {
        return new(true, global::BlockiumLauncher.Shared.Errors.Error.None);
    }

    public static Result Failure(Error Error)
    {
        return new(false, Error);
    }
}
