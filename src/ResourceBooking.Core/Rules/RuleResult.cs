namespace ResourceBooking.Core.Rules;

/// Чтение данных для правил. В ядре — интерфейс; реализация в памяти — InMemoryBookingCheckReader,
/// реализация на PostgreSQL появится в практике 4 и будет читать в транзакции Dispatcher.
public interface IBookingCheckReader
{
    /// Есть ли бронь ресурса в confirmed или used, пересекающая [start, end). Соседние интервалы не пересекаются.
    Task<bool> HasActiveOverlapAsync(Guid resourceId, DateTimeOffset start, DateTimeOffset end, CancellationToken ct);

    /// Сколько у пользователя броней в confirmed или used с End > now.
    Task<int> CountActiveBookingsAsync(Guid userId, DateTimeOffset now, CancellationToken ct);
}
