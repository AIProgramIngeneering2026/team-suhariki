using static ResourceBooking.Core.Bookings.BookingCommand;
using static ResourceBooking.Core.Bookings.BookingStatus;

namespace ResourceBooking.Core.Bookings;

/// Таблица переходов Job: команда | из какого состояния | в какое (docs/job-states.md).
/// Переход проходит, только если в таблице есть строка с этой командой, текущим состоянием и целевым состоянием.
/// Строки нет — InvalidTransitionException, состояние не меняется.
public static class BookingTransitions
{
    private static readonly HashSet<(BookingCommand Command, BookingStatus From, BookingStatus To)> Allowed =
    [
        (BeginCheck, Requested, Requested),
        (Confirm, Requested, Confirmed),
        (Reject, Requested, Rejected),
        (FailCheck, Requested, Requested),       // попытки ещё есть, Dispatcher повторит проверку
        (FailCheck, Requested, CheckFailed),     // попытки кончились
        (ReleaseCheck, Requested, Requested),
        (RecoverAfterRestart, Requested, Requested),
        (TimeOut, Requested, TimedOut),
        (Cancel, Confirmed, Cancelled),
        (CheckIn, Confirmed, Used),
        (Expire, Confirmed, Expired),
    ];

    public static bool IsAllowed(BookingCommand command, BookingStatus from, BookingStatus to) =>
        Allowed.Contains((command, from, to));

    public static IReadOnlyCollection<(BookingCommand Command, BookingStatus From, BookingStatus To)> All => Allowed;
}
