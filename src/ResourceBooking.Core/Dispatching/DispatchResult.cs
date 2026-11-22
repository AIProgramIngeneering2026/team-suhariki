using ResourceBooking.Core.Bookings;

namespace ResourceBooking.Core.Dispatching;

public enum DispatchOutcome
{
    /// Все Tool вернули Pass → confirmed.
    Confirmed,

    /// Tool вернул Reject → rejected с причиной.
    Rejected,

    /// Tool упал (ошибка в коде), попытки ещё есть → Job остаётся requested.
    FailedWillRetry,

    /// Tool упал в последний раз → check_failed.
    CheckFailed,

    /// Временный сбой чтения → Job остаётся requested, попытка не засчитана.
    Unavailable,

    /// Наступил start: проверку не начинаем / итог не сохраняем, Job переведёт в timed_out таймер.
    DeadlinePassed,

    /// Итог Tool пришёл, но Job его не принял (например, после перезапуска или таймаута). Лог transition_rejected.
    ResultDiscarded
}

public sealed record DispatchResult(DispatchOutcome Outcome, BookingStatus Status, string? Detail = null);
