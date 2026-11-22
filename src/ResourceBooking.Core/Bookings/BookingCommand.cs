namespace ResourceBooking.Core.Bookings;

/// Команды, которые меняют Job. Соответствие командам упражнения «Взлом Job»:
/// start → BeginCheck, finish → Confirm / Reject, fail → FailCheck, restart → RecoverAfterRestart.
public enum BookingCommand
{
    /// Dispatcher запускает проверку (Tool). Пока проверка идёт, вторую запустить нельзя.
    BeginCheck,

    /// Все правила вернули Pass.
    Confirm,

    /// Правило вернуло Reject.
    Reject,

    /// Правило упало с ошибкой в коде. Попытка засчитывается.
    FailCheck,

    /// Временный сбой (RuleUnavailableException) или остановка сервиса: попытка не засчитывается.
    ReleaseCheck,

    /// Сервер перезапустился: работавшая проверка пропала, ответа от неё не будет.
    RecoverAfterRestart,

    /// TimerWorker: наступил start, итог проверки не зафиксирован.
    TimeOut,

    /// Владелец отменил подтверждённую бронь.
    Cancel,

    /// Ответственный отметил использование.
    CheckIn,

    /// TimerWorker: наступил end, отметки нет.
    Expire
}
