namespace ResourceBooking.Core.Rules;

/// Registry: хранит зарегистрированные Tool (IBookingRule) и выдаёт их по имени операции — коду правила.
public interface IBookingRuleRegistry
{
    /// Tool по имени операции. Неизвестная операция — UnknownOperationException (unknown_operation).
    IBookingRule Get(string code);

    /// Все правила в порядке Order — так их запускает Dispatcher.
    IReadOnlyList<IBookingRule> GetRules();
}
