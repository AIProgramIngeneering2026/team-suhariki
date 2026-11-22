using ResourceBooking.Core.Bookings;

namespace ResourceBooking.Core.Storage;

/// Хранилище в памяти. Один замок на всё хранилище играет роль транзакции с условным UPDATE:
/// проверка перехода и его сохранение происходят без вмешательства параллельных вызовов.
public sealed class InMemoryBookingRepository : IBookingRepository
{
    private readonly Dictionary<Guid, Booking> _bookings = new();
    private readonly object _gate = new();

    public Task AddAsync(Booking booking, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(booking);
        lock (_gate)
        {
            if (!_bookings.TryAdd(booking.Id, booking))
                throw new InvalidOperationException($"Бронь {booking.Id} уже существует");
        }

        return Task.CompletedTask;
    }

    public Task<Booking> GetAsync(Guid id, CancellationToken ct = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_bookings.TryGetValue(id, out var booking)
                ? booking
                : throw new BookingNotFoundException(id));
        }
    }

    public Task<T> UpdateAsync<T>(Guid id, Func<Booking, T> change, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        lock (_gate)
        {
            if (!_bookings.TryGetValue(id, out var booking))
                throw new BookingNotFoundException(id);

            return Task.FromResult(change(booking));
        }
    }

    public Task<IReadOnlyList<Booking>> FindAsync(Func<Booking, bool> predicate, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        lock (_gate)
        {
            IReadOnlyList<Booking> found = _bookings.Values.Where(predicate).ToList();
            return Task.FromResult(found);
        }
    }
}
