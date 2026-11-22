namespace ResourceBooking.Core.Dispatching;

/// Источник времени. В приложении — время БД (ADR-0005), в тестах — подставные часы.
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
