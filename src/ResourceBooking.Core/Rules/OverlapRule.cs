namespace ResourceBooking.Core.Rules;

/// Временный сбой при чтении данных правилом: хранилище недоступно, таймаут запроса.
/// Dispatcher снимает проверку без засчитанной попытки, Job остаётся requested, проверка повторится до дедлайна.
public sealed class RuleUnavailableException : Exception
{
    public RuleUnavailableException(string message, Exception? inner = null) : base(message, inner)
    {
    }
}
