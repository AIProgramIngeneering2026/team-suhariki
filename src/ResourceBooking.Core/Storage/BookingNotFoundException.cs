using ResourceBooking.Core.Errors;

namespace ResourceBooking.Core.Storage;

/// 404 booking_not_found.
public sealed class BookingNotFoundException : DomainException
{
    public const string ErrorCode = "booking_not_found";

    public BookingNotFoundException(Guid id) : base(ErrorCode, $"Бронь {id} не найдена")
    {
        BookingId = id;
    }

    public Guid BookingId { get; }
}
