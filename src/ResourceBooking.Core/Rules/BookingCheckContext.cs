namespace ResourceBooking.Core.Rules;

/// Tool: одна проверка брони (docs/tool-contract.md). Реализация не меняет состояние Job и не пишет в хранилище.
public interface IBookingRule
{
    /// Уникальный код правила — «имя операции» для Registry, логов и метрик, например "overlap".
    string Code { get; }

    /// Порядок выполнения: меньше — раньше. Дешёвые проверки идут первыми.
    int Order { get; }

    /// Проверяет бронь. Ожидаемый отказ — RuleResult.Reject, а не исключение.
    /// Временный сбой чтения — RuleUnavailableException; любое другое исключение — ошибка в коде правила.
    Task<RuleResult> CheckAsync(BookingCheckContext context, CancellationToken ct);
}
