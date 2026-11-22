namespace ResourceBooking.Core.Errors;

/// «Неверные параметры»: код invalid_params и поле, которое нарушено.
/// По контракту Tool (docs/tool-contract.md, «Кто проверяет параметры») параметры проверяет вызывающий
/// до создания Job, а Tool получает уже проверенный контекст и формат не перепроверяет.
public sealed class InvalidParamsException : DomainException
{
    public const string ErrorCode = "invalid_params";

    public InvalidParamsException(string field, string message)
        : base(ErrorCode, $"{field}: {message}")
    {
        Field = field;
    }

    public string Field { get; }
}
