using ResourceBooking.Core.Bookings;

namespace ResourceBooking.Core.Storage;

/// Хранение Job. Ядро зависит только от этого интерфейса; сейчас реализация в памяти,
/// на практике 4 — PostgreSQL (условный UPDATE в транзакции, ADR-0002).
public interface IBookingRepository
{
    Task AddAsync(Booking booking, CancellationToken ct = default);

    /// Бронь по id; нет — BookingNotFoundException.
    Task<Booking> GetAsync(Guid id, CancellationToken ct = default);

    /// Атомарно применяет изменение к брони: никто другой не меняет её одновременно.
    /// Если change бросает исключение, бронь остаётся прежней (Booking проверяет переход до изменения полей).
    Task<T> UpdateAsync<T>(Guid id, Func<Booking, T> change, CancellationToken ct = default);

    Task<IReadOnlyList<Booking>> FindAsync(Func<Booking, bool> predicate, CancellationToken ct = default);
}
