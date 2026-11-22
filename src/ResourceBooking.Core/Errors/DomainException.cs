namespace ResourceBooking.Core.Errors;

/// Базовая ошибка доменного ядра. Code — машинный код из единого формата ошибок API
/// (components.schemas.Error в docs/api/openapi.yaml); HTTP-слой переводит его в ответ.
public abstract class DomainException : Exception
{
    protected DomainException(string code, string message) : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}
