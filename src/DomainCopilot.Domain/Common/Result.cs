namespace DomainCopilot.Domain.Common;

public readonly struct Result<T>
{
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public T Value { get; }
    public DomainError? Error { get; }

    private Result(bool isSuccess, T value, DomainError? error)
    {
        IsSuccess = isSuccess;
        Value = value;
        Error = error;
    }

    public static Result<T> Success(T value) => new(true, value, null);
    public static Result<T> Failure(DomainError error) => new(false, default!, error);
}

public readonly struct Unit
{
    public static readonly Unit Value = new();
}
