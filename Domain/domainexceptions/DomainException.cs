public class DomainException : Exception
{
    public string ErrorCode { get; }
    public string? Field { get; }
    public object? Value { get; }
    public DateTime OccurredAt { get; } = DateTime.UtcNow;

    public DomainException(
        string message,
        string errorCode = "Unknown",
        string? field = null,
        object? value = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
        Field = field;
        Value = value;
    }

    /// <summary>
    /// Виртуальный метод для структурированного логирования.
    /// </summary>
    public virtual object ToLogObject()
    {
        return new
        {
            ErrorCode,
            Field,
            Value,
            Message,
            ExceptionType = GetType().Name,
            OccurredAt,
            InnerException = InnerException?.Message
        };
    }

    public override string ToString()
    {
        var inner = InnerException != null ? $"\nInnerException: {InnerException}" : "";
        return $"[{ErrorCode}] {Message}" +
            (Field != null ? $" (Field: {Field}, Value: {Value})" : "") +
            $" at {OccurredAt:u}" +
            inner;
    }

    /// <summary>
    /// Быстрое создание исключения с типовым кодом
    /// </summary>
    public static DomainException Create(string code, string message, string? field = null, object? value = null, Exception? inner = null)
        => new DomainException(message, code, field, value, inner);
}
