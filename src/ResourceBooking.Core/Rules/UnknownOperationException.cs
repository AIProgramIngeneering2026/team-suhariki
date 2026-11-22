namespace ResourceBooking.Core.Rules;

/// Registry в памяти. В приложении правила придут из DI (IEnumerable&lt;IBookingRule&gt;);
/// новый Tool добавляется классом и регистрацией, без правки Registry и Dispatcher.
public sealed class BookingRuleRegistry : IBookingRuleRegistry
{
    private readonly Dictionary<string, IBookingRule> _byCode;
    private readonly IReadOnlyList<IBookingRule> _ordered;

    /// Пустой список и повтор кода — ошибка конфигурации: приложение не стартует.
    public BookingRuleRegistry(IEnumerable<IBookingRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        _byCode = new Dictionary<string, IBookingRule>(StringComparer.Ordinal);
        foreach (var rule in rules)
        {
            if (string.IsNullOrWhiteSpace(rule.Code))
                throw new InvalidOperationException($"У правила {rule.GetType().Name} пустой Code");
            if (!_byCode.TryAdd(rule.Code, rule))
                throw new InvalidOperationException($"Код правила «{rule.Code}» зарегистрирован дважды");
        }

        if (_byCode.Count == 0)
            throw new InvalidOperationException("Не зарегистрировано ни одного правила проверки брони");

        _ordered = _byCode.Values.OrderBy(r => r.Order).ThenBy(r => r.Code, StringComparer.Ordinal).ToList();
    }

    public IBookingRule Get(string code)
    {
        if (code is not null && _byCode.TryGetValue(code, out var rule))
            return rule;

        throw new UnknownOperationException(code ?? "");
    }

    public IReadOnlyList<IBookingRule> GetRules() => _ordered;
}
