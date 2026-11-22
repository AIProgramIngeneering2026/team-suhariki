namespace ResourceBooking.Core.Rules;

/// Вход Tool. Собирает Dispatcher из сохранённого Job; все поля уже прошли проверку формата
/// при создании брони (Booking.Create), поэтому Tool их не перепроверяет.
public sealed record BookingCheckContext(
    Guid BookingId,
    Guid UserId,
    Guid ResourceId,
    DateTimeOffset Start,          // включается
    DateTimeOffset End,            // не включается, End > Start
    DateTimeOffset Now,            // время на момент попытки; заново на каждой попытке
    IBookingCheckReader Reader);   // доступ к данным только на чтение
