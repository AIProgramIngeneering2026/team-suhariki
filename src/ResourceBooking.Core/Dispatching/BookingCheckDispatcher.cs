using ResourceBooking.Core.Bookings;
using ResourceBooking.Core.Rules;
using ResourceBooking.Core.Storage;

namespace ResourceBooking.Core.Dispatching;

/// Dispatcher: берёт Tool из Registry, ведёт Job по состояниям и превращает ответ Tool — и его падение —
/// в переход Job. Конкретных Tool не знает: работает только с IBookingRule и IBookingRuleRegistry,
/// поэтому новый Tool добавляется регистрацией, без правки этого класса.
///
/// Решения о допустимости переходов принимает Job; Dispatcher проверяет только время (до start).
public sealed class BookingCheckDispatcher
{
    private readonly IBookingRepository _bookings;
    private readonly IBookingRuleRegistry _rules;
    private readonly IBookingCheckReader _reader;
    private readonly IClock _clock;
    private readonly int _maxFailedChecks;

    public BookingCheckDispatcher(
        IBookingRepository bookings,
        IBookingRuleRegistry rules,
        IBookingCheckReader reader,
        IClock clock,
        int maxFailedChecks = Booking.DefaultMaxFailedChecks)
    {
        _bookings = bookings;
        _rules = rules;
        _reader = reader;
        _clock = clock;
        _maxFailedChecks = maxFailedChecks;
    }

    /// Проверяет одну бронь. Если Job нельзя взять в проверку (не requested, проверка уже идёт) —
    /// InvalidTransitionException от Job.
    public async Task<DispatchResult> DispatchAsync(Guid bookingId, CancellationToken ct = default)
    {
        var startedAt = _clock.UtcNow;

        // start: атомарно — проверка «ещё не идёт» и её запуск в одном изменении (защита от З1).
        var started = await _bookings.UpdateAsync(bookingId, b =>
            b.Status == BookingStatus.Requested && startedAt >= b.Start ? (int?)null : b.BeginCheck(), ct);

        var booking = await _bookings.GetAsync(bookingId, ct);
        if (started is not { } attempt)
            return new DispatchResult(DispatchOutcome.DeadlinePassed, booking.Status);

        var context = new BookingCheckContext(
            booking.Id, booking.UserId, booking.ResourceId, booking.Start, booking.End, startedAt, _reader);

        RuleResult verdict = RuleResult.Pass();
        string? currentRule = null;
        try
        {
            foreach (var rule in _rules.GetRules())
            {
                currentRule = rule.Code;
                var result = await rule.CheckAsync(context, ct)
                             ?? throw new InvalidOperationException("Правило вернуло null");
                if (!result.Passed)
                {
                    verdict = result;
                    break;
                }
            }
        }
        catch (RuleUnavailableException ex)
        {
            return await SaveAsync(bookingId, b => b.ReleaseCheck(attempt),
                DispatchOutcome.Unavailable, $"{currentRule}: {ex.Message}");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Остановка сервиса: попытка не засчитывается, Job снова будет взят в проверку.
            await SaveAsync(bookingId, b => b.ReleaseCheck(attempt), DispatchOutcome.Unavailable, null);
            throw;
        }
        catch (Exception ex)
        {
            // Падение Tool → переход Job: повтор или check_failed.
            var error = $"rule_failed {currentRule}: {ex.Message}";
            return await SaveAsync(bookingId, b =>
                {
                    b.FailCheck(attempt, error, _maxFailedChecks);
                },
                status => status == BookingStatus.CheckFailed
                    ? DispatchOutcome.CheckFailed
                    : DispatchOutcome.FailedWillRetry,
                error);
        }

        var finishedAt = _clock.UtcNow;
        if (finishedAt >= booking.Start)
        {
            return await SaveAsync(bookingId, b => b.ReleaseCheck(attempt),
                DispatchOutcome.DeadlinePassed, "итог получен после start");
        }

        return verdict.Passed
            ? await SaveAsync(bookingId, b => b.Confirm(attempt), DispatchOutcome.Confirmed, null)
            : await SaveAsync(bookingId, b => b.Reject(attempt, verdict.Reason!.Value.ToApiCode()),
                DispatchOutcome.Rejected, verdict.Message);
    }

    /// Проверяет все брони requested без идущей проверки и с ещё не наступившим start.
    public async Task<IReadOnlyList<DispatchResult>> DispatchPendingAsync(CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        var pending = await _bookings.FindAsync(b =>
            b.Status == BookingStatus.Requested && !b.IsCheckRunning && b.Start > now, ct);

        var results = new List<DispatchResult>(pending.Count);
        foreach (var booking in pending)
            results.Add(await DispatchAsync(booking.Id, ct));
        return results;
    }

    /// Вызывается при старте процесса: проверки, шедшие до перезапуска, пропали — снимаем их (защита от З2).
    public async Task<int> RecoverAfterRestartAsync(CancellationToken ct = default)
    {
        var stuck = await _bookings.FindAsync(b => b.Status == BookingStatus.Requested && b.IsCheckRunning, ct);
        foreach (var booking in stuck)
            await _bookings.UpdateAsync(booking.Id, b =>
            {
                if (b.Status == BookingStatus.Requested)
                    b.RecoverAfterRestart();
                return 0;
            }, ct);
        return stuck.Count;
    }

    private Task<DispatchResult> SaveAsync(Guid id, Action<Booking> transition, DispatchOutcome outcome,
        string? detail) => SaveAsync(id, transition, _ => outcome, detail);

    /// Сохраняет переход. Если Job его не принял — итог отбрасывается (лог transition_rejected в практике 4).
    private async Task<DispatchResult> SaveAsync(Guid id, Action<Booking> transition,
        Func<BookingStatus, DispatchOutcome> outcome, string? detail)
    {
        try
        {
            var status = await _bookings.UpdateAsync(id, b =>
            {
                transition(b);
                return b.Status;
            }, CancellationToken.None);
            return new DispatchResult(outcome(status), status, detail);
        }
        catch (InvalidTransitionException ex)
        {
            return new DispatchResult(DispatchOutcome.ResultDiscarded, ex.From, ex.Message);
        }
    }
}
