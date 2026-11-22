using ResourceBooking.Core.Errors;

namespace ResourceBooking.Core.Bookings;

/// Job — бронь одного ресурса на интервал [Start, End).
///
/// Правило переходов живёт здесь, а не в вызывающем коде: любой метод сначала сверяется
/// с BookingTransitions и с состоянием проверки, и только потом меняет состояние.
/// Недопустимый переход — InvalidTransitionException, состояние при этом не меняется.
///
/// Временные условия («до start», «наступил end») Job не проверяет — по architecture.md их проверяет
/// одно место: Dispatcher / TimerWorker по времени БД (в ядре — по IClock).
///
/// Класс не потокобезопасен: менять его нужно через IBookingRepository.UpdateAsync, который
/// выполняет изменение атомарно (аналог условного UPDATE в транзакции).
public sealed class Booking
{
    public const int DefaultMaxFailedChecks = 3;

    private Booking(Guid id, Guid userId, Guid resourceId, DateTimeOffset start, DateTimeOffset end)
    {
        Id = id;
        UserId = userId;
        ResourceId = resourceId;
        Start = start;
        End = end;
        Status = BookingStatus.Requested;
    }

    public Guid Id { get; }
    public Guid UserId { get; }
    public Guid ResourceId { get; }
    public DateTimeOffset Start { get; }
    public DateTimeOffset End { get; }

    public BookingStatus Status { get; private set; }

    /// Код причины отклонения (reason.code в API), только для Rejected.
    public string? RejectReasonCode { get; private set; }

    /// Идёт ли сейчас проверка (работает ли Tool). Не больше одной одновременно — защита от З1.
    public bool IsCheckRunning { get; private set; }

    /// Номер последней запущенной проверки. Итог принимается только от неё: ответ «потерянного» Tool
    /// после перезапуска или после повтора отклоняется (З2, З4).
    public int CheckAttempt { get; private set; }

    /// Сколько раз правило упало с ошибкой в коде.
    public int FailedChecks { get; private set; }

    public string? LastCheckError { get; private set; }

    /// Создаёт Job в состоянии requested. Здесь — синхронная проверка параметров вызывающим
    /// (BookingService по tool-contract.md): ошибка «неверные параметры» с указанием поля.
    public static Booking Create(Guid id, Guid userId, Guid resourceId,
        DateTimeOffset start, DateTimeOffset end, DateTimeOffset now)
    {
        if (id == Guid.Empty)
            throw new InvalidParamsException("id", "идентификатор брони обязателен");
        if (userId == Guid.Empty)
            throw new InvalidParamsException("userId", "пользователь обязателен");
        if (resourceId == Guid.Empty)
            throw new InvalidParamsException("resourceId", "ресурс обязателен");
        if (end <= start)
            throw new InvalidParamsException("end", "окончание должно быть позже начала");
        if (start <= now)
            throw new InvalidParamsException("start", "начало интервала должно быть в будущем");

        return new Booking(id, userId, resourceId, start, end);
    }

    /// start: запускает проверку и возвращает её номер. Вторая параллельная проверка — отказ (З1).
    public int BeginCheck()
    {
        EnsureAllowed(BookingCommand.BeginCheck, BookingStatus.Requested);
        if (IsCheckRunning)
            throw Invalid(BookingCommand.BeginCheck, $"проверка #{CheckAttempt} уже выполняется");

        CheckAttempt++;
        IsCheckRunning = true;
        return CheckAttempt;
    }

    /// finish (успех): все правила вернули Pass.
    public void Confirm(int attempt)
    {
        EnsureAllowed(BookingCommand.Confirm, BookingStatus.Confirmed);
        EnsureCurrentCheck(BookingCommand.Confirm, attempt);

        IsCheckRunning = false;
        Status = BookingStatus.Confirmed;
    }

    /// finish (отказ правила): бронь отклонена с причиной.
    public void Reject(int attempt, string reasonCode)
    {
        if (string.IsNullOrWhiteSpace(reasonCode))
            throw new InvalidParamsException("reasonCode", "код причины обязателен");

        EnsureAllowed(BookingCommand.Reject, BookingStatus.Rejected);
        EnsureCurrentCheck(BookingCommand.Reject, attempt);

        IsCheckRunning = false;
        RejectReasonCode = reasonCode;
        Status = BookingStatus.Rejected;
    }

    /// fail: правило упало с ошибкой в коде. После maxFailedChecks падений — CheckFailed.
    public void FailCheck(int attempt, string error, int maxFailedChecks = DefaultMaxFailedChecks)
    {
        if (maxFailedChecks < 1)
            throw new InvalidParamsException("maxFailedChecks", "должно быть не меньше 1");

        var target = FailedChecks + 1 >= maxFailedChecks ? BookingStatus.CheckFailed : BookingStatus.Requested;
        EnsureAllowed(BookingCommand.FailCheck, target);
        EnsureCurrentCheck(BookingCommand.FailCheck, attempt);

        IsCheckRunning = false;
        FailedChecks++;
        LastCheckError = error;
        Status = target;
    }

    /// Временный сбой или остановка: проверка снимается без засчитанной попытки, Job остаётся requested.
    public void ReleaseCheck(int attempt)
    {
        EnsureAllowed(BookingCommand.ReleaseCheck, BookingStatus.Requested);
        EnsureCurrentCheck(BookingCommand.ReleaseCheck, attempt);

        IsCheckRunning = false;
    }

    /// restart: работавшая проверка пропала. Job остаётся requested и снова доступен Dispatcher,
    /// а ответ пропавшей проверки уже не будет принят (у неё старый номер).
    public void RecoverAfterRestart()
    {
        EnsureAllowed(BookingCommand.RecoverAfterRestart, BookingStatus.Requested);
        IsCheckRunning = false;
    }

    /// TimerWorker, наступил start. Проходит и во время проверки: дедлайн важнее, поздний итог будет отклонён.
    public void TimeOut()
    {
        EnsureAllowed(BookingCommand.TimeOut, BookingStatus.TimedOut);
        IsCheckRunning = false;
        Status = BookingStatus.TimedOut;
    }

    /// Владелец отменяет подтверждённую бронь (ADR-0004: отменить можно только confirmed).
    public void Cancel()
    {
        EnsureAllowed(BookingCommand.Cancel, BookingStatus.Cancelled);
        Status = BookingStatus.Cancelled;
    }

    /// Ответственный отмечает использование.
    public void CheckIn()
    {
        EnsureAllowed(BookingCommand.CheckIn, BookingStatus.Used);
        Status = BookingStatus.Used;
    }

    /// TimerWorker, наступил end без отметки.
    public void Expire()
    {
        EnsureAllowed(BookingCommand.Expire, BookingStatus.Expired);
        Status = BookingStatus.Expired;
    }

    private void EnsureAllowed(BookingCommand command, BookingStatus to)
    {
        if (!BookingTransitions.IsAllowed(command, Status, to))
            throw Invalid(command, $"нет перехода в {to.ToApiCode()}");
    }

    private void EnsureCurrentCheck(BookingCommand command, int attempt)
    {
        if (!IsCheckRunning)
            throw Invalid(command, "проверка не запущена");
        if (attempt != CheckAttempt)
            throw Invalid(command, $"итог устаревшей проверки #{attempt}, текущая — #{CheckAttempt}");
    }

    private InvalidTransitionException Invalid(BookingCommand command, string reason) =>
        new(command, Status, reason);
}
