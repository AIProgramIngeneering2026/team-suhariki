# Состояния Job (брони)

Job — бронь одного ресурса на интервал `[start, end)`. Переходы и условия взяты из [requirements.md](requirements.md),
раздел «Переходы Job». Код состояния — значение поля `status` в [API](api/openapi.yaml).

| Состояние | Код в API | Конечное | Занимает ресурс и лимит |
|---|---|---|---|
| Запрошена | `requested` | нет | нет |
| Подтверждена | `confirmed` | нет | да |
| Отклонена | `rejected` | да | нет |
| Просрочена | `timed_out` | да | нет |
| Отменена | `cancelled` | да | нет |
| Использована | `used` | да | да, до окончания интервала |
| Истекла | `expired` | да | нет |
| Проверка не удалась | `check_failed` | да | нет |

## Диаграмма

```mermaid
stateDiagram-v2
    [*] --> requested : POST /api/bookings (202)

    requested --> confirmed : Dispatcher, все правила прошли, до start
    requested --> rejected : Dispatcher, правило отказало, до start
    requested --> timed_out : TimerWorker, наступил start
    requested --> check_failed : Dispatcher, правило упало 3 раза подряд

    confirmed --> cancelled : владелец, POST cancel, до start
    confirmed --> used : ответственный, POST check-in, от start до end
    confirmed --> expired : TimerWorker, наступил end

    rejected --> [*]
    timed_out --> [*]
    cancelled --> [*]
    used --> [*]
    expired --> [*]
    check_failed --> [*]
```

Подписи стрелок: кто выполняет переход и условие на момент сохранения. Все переходы выполняются условным
обновлением в одной транзакции: `UPDATE … SET status = :to WHERE id = :id AND status = :from AND <условие по времени>`.
Если обновление не изменило ни одной строки, перехода нет, письмо в очередь не ставится.

## Разрешённые переходы

| Из | В | Кто | Условие на момент сохранения | Письмо |
|---|---|---|---|---|
| `requested` | `confirmed` | Dispatcher | Все `IBookingRule` вернули `Pass`, время < `start` | Подтверждение |
| `requested` | `rejected` | Dispatcher | Хотя бы одно правило вернуло `Reject`, время < `start` | Отклонение с причиной |
| `requested` | `timed_out` | TimerWorker | Время ≥ `start`, результат проверки не зафиксирован | Нет |
| `requested` | `check_failed` | Dispatcher | Правило третий раз подряд упало с ошибкой в коде (не `RuleUnavailableException`) | Нет |
| `confirmed` | `cancelled` | Владелец через API | Время < `start` | Отмена |
| `confirmed` | `used` | Ответственный через API | `start` ≤ время < `end` | Нет |
| `confirmed` | `expired` | TimerWorker | Время ≥ `end` | Истечение |

## Запрещённые переходы и ответ системы

Формат ошибки единый для всего API: `{"error": {"code", "message", "field", "details", "requestId"}}`, см.
`components.schemas.Error` в [openapi.yaml](api/openapi.yaml).

| Попытка | Пример | Ответ системы |
|---|---|---|
| Отменить Job не в `confirmed`: `requested`, `rejected`, `timed_out`, `used`, `expired` — в любое время, в том числе после `start` | E15 | `409 invalid_state`, `details.currentStatus`, состояние не меняется |
| Отменить `confirmed` при времени ≥ `start` | E12, E21 | `409 cancel_window_closed`, `details.start`, бронь остаётся `confirmed` |
| Повторно отменить уже `cancelled` — в любое время, в том числе после `start` | E14 | Не переход, а повтор: `200` с текущей бронью, второе письмо не ставится |
| Отметить использование Job не в `confirmed`: `requested`, `rejected`, `timed_out`, `cancelled`, `expired` — в любое время, в том числе до `start` и после `end` | E20 | `409 invalid_state`, `details.currentStatus` |
| Отметить использование `confirmed` раньше `start` | E17 | `409 check_in_too_early`, `details.start` |
| Отметить использование `confirmed` при времени ≥ `end` | E18, E22 | `409 check_in_window_closed`, `details.end` |
| Повторно отметить уже `used` — в любое время, в том числе после `end` | E19 | Не переход, а повтор: `200` с текущей бронью |
| Отменить или отметить чужую либо несуществующую бронь | E13, E16, E23 | `404 booking_not_found`, одинаково для любого состояния: статус не раскрывается |
| Вернуть конечный Job в работу («воскресить») | К5 | В API нет такой операции. Нужна новая бронь через `POST /api/bookings` с новым `Idempotency-Key`. Старый Job и его причина не меняются |
| Повторить `POST /api/bookings` с тем же ключом для конечного Job | E5 | `200` с исходной бронью в её конечном состоянии; новый Job не создаётся |
| `requested` → `confirmed` или `rejected` при времени ≥ `start` | E10 | Внутренний переход без HTTP: условное обновление не проходит, Dispatcher пишет в лог `transition_rejected`; Job переводит в `timed_out` TimerWorker |
| `confirmed` → `rejected` или `timed_out`; `used` → `expired`; `cancelled` → `expired` | S4, S6 | Внутренний переход: таймер и Dispatcher меняют только Job в своём исходном состоянии, обновление ничего не меняет, письмо не ставится |
| Любой переход из конечного состояния | — | Через API — `409 invalid_state` (для владельца или ответственного) или `404 booking_not_found` (для остальных); внутренние — обновление ничего не меняет |

### Порядок проверок при отмене и отметке

Одна попытка может нарушать сразу несколько условий: например, отмена `requested` после `start`. Чтобы код ответа был
однозначным, проверки идут в фиксированном порядке, и ответ даёт первая неудачная. Порядок взят из шагов S3 и S4.

| Шаг | `POST …/cancel` (S3) | `POST …/check-in` (S4) |
|---|---|---|
| 1. Доступ | Бронь принадлежит пользователю, иначе `404 booking_not_found` | Пользователь закреплён за ресурсом брони, иначе `404 booking_not_found` |
| 2. Повтор | Уже `cancelled` → `200`, время не проверяется (E14) | Уже `used` → `200`, время не проверяется (E19) |
| 3. Состояние | Не `confirmed` → `409 invalid_state`, время не проверяется | Не `confirmed` → `409 invalid_state`, время не проверяется |
| 4. Время | ≥ `start` → `409 cancel_window_closed` | < `start` → `409 check_in_too_early`; ≥ `end` → `409 check_in_window_closed` |

Состояние проверяется раньше времени: окно времени имеет смысл только для `confirmed`, а `invalid_state` сообщает
клиенту больше — бронь уже не изменить никогда, а не «сейчас не время».

Примеры из спорных случаев:

| Попытка | Ответ |
|---|---|
| Отмена `requested` после `start` | `409 invalid_state`, `details.currentStatus = requested` |
| Отметка `requested` раньше `start` | `409 invalid_state`, `details.currentStatus = requested` |
| Повторная отмена `cancelled` после `start` | `200` с текущей бронью (E14) |
| Повторная отметка `used` после `end` | `200` с текущей бронью (E19) |

Шаги 3 и 4 выполняются одним условным обновлением `… WHERE status = 'confirmed' AND <условие по времени>`.
Если оно ничего не изменило, сервис в той же транзакции перечитывает строку и выбирает код по этой таблице:
состояние уже не `confirmed` → `invalid_state` (или `200`, если это повтор шага 2, например параллельная отмена
успела раньше), иначе — код нарушенного окна времени.

## Проверка в ядре: одна за раз

Внутри `requested` Job помнит, идёт ли проверка (`IsCheckRunning`) и её номер (`CheckAttempt`). Команды ядра
(`src/ResourceBooking.Core/Bookings/BookingTransitions.cs`):

| Команда | Из | В | Дополнительное условие |
|---|---|---|---|
| `BeginCheck` | `requested` | `requested` | Проверка не идёт; номер проверки +1 |
| `Confirm` / `Reject` | `requested` | `confirmed` / `rejected` | Идёт проверка с этим номером |
| `FailCheck` | `requested` | `requested` или `check_failed` | Идёт проверка с этим номером; на 3-м падении — `check_failed` |
| `ReleaseCheck` | `requested` | `requested` | Временный сбой или остановка: попытка не засчитывается |
| `RecoverAfterRestart` | `requested` | `requested` | Снимает пропавшую при перезапуске проверку |
| `TimeOut` | `requested` | `timed_out` | Проходит и во время проверки |
| `Cancel` / `CheckIn` / `Expire` | `confirmed` | `cancelled` / `used` / `expired` | — |

Итог устаревшей проверки (после перезапуска, повтора или таймаута) Job отклоняет — так ядро не пропускает
двойную работу, два итога и «вечную работу» (злодейства З1, З2, З4).

В доменном ядре недопустимый переход даёт `InvalidTransitionException(command, from)`. HTTP-слой переводит его в
`409 invalid_state`, а нарушение временного окна — в соответствующий код `cancel_window_closed`, `check_in_too_early`
или `check_in_window_closed`.
