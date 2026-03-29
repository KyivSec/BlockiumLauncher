namespace BlockiumLauncher.Shared.Errors
{
    public sealed record Error
    {
        public static readonly Error None = new("None", "No error.");

        public string Code { get; }
        public string Message { get; }
        public string? Details { get; }

        public Error(string Code, string Message, string? Details = null)
        {
            if (string.IsNullOrWhiteSpace(Code))
            {
                throw new ArgumentException("Error code cannot be null or whitespace.", nameof(Code));
            }

            if (string.IsNullOrWhiteSpace(Message))
            {
                throw new ArgumentException("Error message cannot be null or whitespace.", nameof(Message));
            }

            this.Code = Code;
            this.Message = Message;
            this.Details = Details;
        }

        public override string ToString()
        {
            return Details is null
                ? $"{Code}: {Message}"
                : $"{Code}: {Message} ({Details})";
        }
    }
}

namespace BlockiumLauncher.Shared.Primitives
{
    public readonly record struct Unit
    {
        public static readonly Unit Value = new();
    }
}

namespace BlockiumLauncher.Shared.Results
{
    using BlockiumLauncher.Shared.Errors;

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
}
