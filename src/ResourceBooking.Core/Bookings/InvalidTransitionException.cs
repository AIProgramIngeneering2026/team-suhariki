using ResourceBooking.Core.Errors;

namespace ResourceBooking.Core.Bookings;

/// Недопустимый переход Job. HTTP-слой переводит в 409 invalid_state с details.currentStatus.
public sealed class InvalidTransitionException : DomainException
{
    public const string ErrorCode = "invalid_state";

    public InvalidTransitionException(BookingCommand command, BookingStatus from, string reason)
        : base(ErrorCode, $"Команда {command} недопустима в состоянии {from.ToApiCode()}: {reason}")
    {
        Command = command;
        From = from;
    }

    public BookingCommand Command { get; }

    public BookingStatus From { get; }
}
