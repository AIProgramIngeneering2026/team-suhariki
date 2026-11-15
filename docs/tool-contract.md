# Контракт Tool: правило проверки брони `IBookingRule`

В РесурсБрони Tool — это одна бизнес-проверка брони. Dispatcher берёт Job в состоянии `requested`, получает у Registry
список правил и вызывает их по одному. Dispatcher не знает конкретных правил: новое правило добавляется классом и
регистрацией, без правки Dispatcher. Место в архитектуре — [architecture.md](architecture.md), почему проверка
фоновая — [ADR-0001](adr/0001-async-booking-check.md).

## Интерфейс

```csharp
namespace ResourceBooking.Core.Rules;

/// Tool: одна проверка брони. Реализация не меняет состояние Job и не пишет в базу.
public interface IBookingRule
{
    /// Уникальный код правила для логов и метрик, например "overlap".
    string Code { get; }

    /// Порядок выполнения: меньше — раньше. Дешёвые проверки идут первыми.
    int Order { get; }

    /// Проверяет бронь. Возвращает Pass или Reject с кодом причины.
    /// Ожидаемый отказ — это RuleResult.Reject, а не исключение.
    Task<RuleResult> CheckAsync(BookingCheckContext context, CancellationToken ct);
}

/// Вход Tool. Собирает Dispatcher; все поля уже прошли проверку формата.
public sealed record BookingCheckContext(
    Guid BookingId,
    Guid UserId,
    Guid ResourceId,
    DateTimeOffset Start,          // включается
    DateTimeOffset End,            // не включается, End > Start
    DateTimeOffset Now,            // время БД в начале проверки
    IBookingCheckReader Reader);   // чтение в транзакции подтверждения, под блокировкой пользователя и ресурса

/// Результат Tool (ToolResult).
public sealed record RuleResult
{
    public bool Passed { get; }
    public RejectReason? Reason { get; }   // null, если Passed

    public static RuleResult Pass();
    public static RuleResult Reject(RejectReason reason, string message);
}

/// Коды причин совпадают с ReasonCode в docs/api/openapi.yaml.
public enum RejectReason
{
    NoPermission,          // no_permission — «Нет права на бронирование»
    OutsideAvailability,   // outside_availability — «Вне часов доступности ресурса»
    IntervalTaken,         // interval_taken — «Интервал занят»
    LimitExceeded          // limit_exceeded — «Превышен лимит»
}

/// Временный сбой при проверке: БД недоступна, таймаут блокировки.
/// Dispatcher откатывает транзакцию, Job остаётся requested, проверка повторяется до дедлайна.
public sealed class RuleUnavailableException : Exception { }
```

## Что получает Tool

`BookingCheckContext` — неизменяемый снимок брони и доступ к данным только на чтение. Tool не получает HTTP-запрос,
EF `DbContext` или сам объект Job: он не может сменить состояние или записать в базу. `Reader` работает внутри той же
транзакции, в которой Dispatcher потом сохранит переход, — поэтому пересечения и лимит проверяются атомарно
с подтверждением (требование из раздела «Участники и правила»).

## Что возвращает Tool

- `RuleResult.Pass()` — проверка пройдена.
- `RuleResult.Reject(reason, message)` — бронь нарушает правило. Код причины попадает в `reason.code` брони в API.

Dispatcher идёт по правилам в порядке `Order` и останавливается на первом `Reject`. Если все вернули `Pass`, он
переводит Job в `confirmed`; при `Reject` — в `rejected` с причиной этого правила. Оба перехода выполняются
условным обновлением с проверкой «время сохранения < `start`» ([job-states.md](job-states.md)).

## Какими ошибками отказывает

| Ситуация | Как сообщает Tool | Что делает Dispatcher | Что видит клиент |
|---|---|---|---|
| Бронь нарушает правило | `RuleResult.Reject(reason, message)` | `requested` → `rejected` с причиной, письмо в очередь | `status: rejected`, `reason.code` |
| Временный сбой (БД, таймаут блокировки) | `RuleUnavailableException` | Откат транзакции, повтор через 5 с, затем 15 с, затем 60 с, пока не наступил `start` | `status: requested`; после `start` — `timed_out` по таймеру |
| Ошибка в коде правила (любое другое исключение) | Исключение | Откат, лог `rule_failed` с `Code` правила и `BookingId`, повтор как при временном сбое | То же, что при временном сбое: Job не подтверждается «по умолчанию» |
| `CancellationToken` отменён (остановка сервиса) | `OperationCanceledException` | Откат; после запуска Job снова будет взят в проверку (К9) | `status: requested` |

Tool никогда не возвращает `Pass` при сомнении: при любой ошибке бронь не подтверждается.

## Кто проверяет параметры

| Что проверяем | Кто | Где |
|---|---|---|
| Формат JSON, типы, обязательные поля, лишние поля, размер тела | HTTP API, синхронно | `400 invalid_params`, `413 payload_too_large` |
| `end > start`, `start` в будущем, ресурс существует | BookingService, синхронно до создания Job | `400 invalid_interval`, `400 interval_started`, `404 resource_not_found` |
| Права, часы доступности, пересечения, лимит | Tool, в фоне | `rejected` с причиной |

Tool получает только уже проверенные параметры и **не перепроверяет формат**: `End > Start`, идентификаторы
существуют. Это гарантия вызывающего (Dispatcher собирает контекст из сохранённого Job). Tool проверяет только
бизнес-условие, ради которого существует.

## Registry

```csharp
public interface IBookingRuleRegistry
{
    /// Все правила в порядке Order. Пустой список — ошибка конфигурации, приложение не стартует.
    IReadOnlyList<IBookingRule> GetRules();
}
```

Правила регистрируются в DI: `services.AddSingleton<IBookingRule, OverlapRule>()`. При старте Registry проверяет, что
коды правил уникальны и список не пуст, иначе приложение завершается с ошибкой конфигурации.

## Tool проекта через этот контракт

| Code | Order | Класс | Проверяет | Причина при отказе | Сценарий |
|---|---|---|---|---|---|
| `access` | 10 | `ResourceAccessRule` | У пользователя есть право бронировать ресурс на момент проверки | `no_permission` | E4 |
| `availability_hours` | 20 | `AvailabilityHoursRule` | Весь интервал `[start, end)` укладывается в часы доступности ресурса | `outside_availability` | E9 |
| `overlap` | 30 | `OverlapRule` | Нет брони `confirmed` или `used` того же ресурса на пересекающемся интервале | `interval_taken` | E2 |
| `user_limit` | 40 | `UserLimitRule` | У пользователя меньше трёх броней `confirmed` или `used` с `end` позже текущего времени | `limit_exceeded` | E3, E7 |

### `OverlapRule` — подробно

- **Вход:** `ResourceId`, `Start`, `End`, `Reader`.
- **Логика:** `Reader.HasActiveOverlapAsync(resourceId, start, end)` ищет брони ресурса в `confirmed` или `used`,
  у которых `existing.Start < End` и `Start < existing.End`. Соседние интервалы (10:00–11:00 и 11:00–12:00) не
  пересекаются. Брони `requested` не учитываются.
- **Атомарность:** Dispatcher перед вызовом правил берёт блокировку строки ресурса (`SELECT … FOR UPDATE`). Второй
  параллельный запрос на тот же ресурс ждёт, пока первый не зафиксирует подтверждение, и затем видит его бронь (E2).
- **Результат:** найдено пересечение → `Reject(IntervalTaken, "Интервал занят")`, иначе `Pass`.
- **Ошибки:** таймаут блокировки или сбой БД → `RuleUnavailableException`.

### `UserLimitRule` — подробно

- **Вход:** `UserId`, `Now`, `Reader`.
- **Логика:** `Reader.CountActiveBookingsAsync(userId, now)` считает брони пользователя в `confirmed` или `used`
  с `End > now`. Три и больше → отказ. Текущий Job в счёт не входит: он ещё `requested`.
- **Атомарность:** Dispatcher блокирует строку пользователя (`SELECT … FOR UPDATE`) в той же транзакции. Два
  параллельных запроса одного пользователя с разными ключами проверяются по очереди: подтверждается один, второй
  получает `limit_exceeded` (E7).
- **Результат:** `Reject(LimitExceeded, "Превышен лимит")` или `Pass`.
- **Ошибки:** сбой БД → `RuleUnavailableException`.

### `ResourceAccessRule` и `AvailabilityHoursRule` — кратко

- `ResourceAccessRule` читает права пользователя на ресурс в момент проверки, а не в момент поиска, поэтому
  право, отозванное после поиска, даёт `no_permission` (E4).
- `AvailabilityHoursRule` сравнивает интервал с расписанием ресурса в часовом поясе организации. Интервал, частично
  выходящий за часы, даёт `outside_availability` (E9).
