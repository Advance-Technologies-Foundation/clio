# Цільова структура для підтримки кількох версій платформи

При еволюції Creatio змінюється набір компонентів, властивостей і поведінок. AI має отримувати каталог, відфільтрований за версією платформи, з якою працює.

## Ключове відкриття: platform-version probe ДОСТУПНИЙ

`/rest/CreatioApiGateway/GetSysInfo` повертає `SysInfo.CoreVersion` (наприклад `8.1.5.xxxx`) — реалізовано в [CreatioApiGateway.cs:705](../cliogate/Files/cs/CreatioApiGateway.cs#L705). Це cliogate-package endpoint (мін. версія `2.0.0.32` — див. [GetCreatioInfoCommand.cs:38](../clio/Command/GetCreatioInfoCommand.cs#L38)). Тобто можна автоматично резолвити версію за `environment-name` із soft-fallback, коли cliogate не встановлений.

**Висновок:** `target-version` стає **опціональним**, а не обов'язковим параметром.

## Поточна структура (baseline)

| Аспект | Стан |
|---|---|
| Сховище | один файл [ComponentRegistry.json](../clio/Command/McpServer/Data/ComponentRegistry.json) (92 записи) |
| Loader | [ComponentInfoCatalog.cs](../clio/Command/McpServer/Tools/ComponentInfoCatalog.cs) — `Lazy<>`, in-memory, fail-on-duplicate |
| Модель | `ComponentRegistryEntry` (`componentType`, `category`, `description`, `container`, `parentTypes`, `properties`, `typicalChildren`, `example`) — **жодного поля версії** |
| Property | `ComponentPropertyDefinition` (`type`, `description`, `required`, `default`, `values`) — **жодного поля версії** |
| MCP tool args | `component-type`, `search` — без `environment-name`, без `target-version` |
| Категорії | hardcoded у двох місцях |
| Version probe | існує (`GetSysInfo.CoreVersion`), але **в каталозі не використовується** |

## Що має підтримувати цільова структура

1. **Component-level versioning** — новий компонент у v8.2 невидимий під v8.1.
2. **Property-level versioning** — нова властивість на існуючому компоненті з'являється у певній версії.
3. **Deprecation** — властивість/компонент видалені у певній версії.
4. **Backwards-compatibility** — записи без version-метаданих = «завжди застосовно», zero-break міграція.
5. **Visibility у відповіді** — AI повинен бачити, проти якої версії відфільтровано (інакше дебаг неможливий).
6. **Категорії — data-driven** (нові категорії приходять із платформою).

## Цільова форма запису

Per-entry availability metadata в одному файлі — найкращий tradeoff для масштабу ~100 × ~5–10 версій: один source-of-truth, diff-friendly у Git, дешевий filter-on-read, нульова дублікація.

```json
{
  "componentType": "crt.Button",
  "category": "interactive",
  "availability": { "since": "8.0.0" },
  "description": "Clickable action element.",
  "properties": {
    "type":     { "type": "string", "description": "Must be crt.Button." },
    "caption":  { "type": "string", "description": "Button caption." },
    "iconPosition": {
      "type": "string",
      "values": ["left", "right"],
      "availability": { "since": "8.1.0" },
      "description": "Icon placement (added in 8.1)."
    },
    "legacyStyleMode": {
      "type": "string",
      "availability": { "since": "8.0.0", "until": "8.2.0" },
      "description": "Pre-8.2 style override. Removed in 8.2; use crt.ButtonToggleGroup."
    }
  }
}
```

Семантика `availability`:
- **відсутність** → завжди застосовно (back-compat — існуючі 92 записи не переписуємо)
- **`since`** — інклюзивно (з цієї версії і пізніше)
- **`until`** — ексклюзивно (до цієї версії, не включно)
- запис фільтрується, коли `target < since` АБО `target >= until`

## Resolution stack для `target-version`

Пріоритет (від високого до низького):

1. **Явний `target-version`** в args інструменту — використовується як є, overrides все.
2. **`environment-name`** → probe `GetSysInfo` → cache per environment. У відповіді `resolvedFrom: "environment"`.
3. **Будь-який fallback** (cliogate недоступний, probe failed, env не задано, version-string не парситься) → **`latest known`** із маркером `resolvedFrom: "latest-fallback"`.

У response завжди повертати:

```json
{
  "resolvedTargetVersion": "8.3.2",
  "resolvedFrom": "latest-fallback"
}
```

Допустимі значення `resolvedFrom`: `"explicit"` | `"environment"` | `"latest-fallback"`.

Це закриває діагностику: AI бачить, чому компонент відсутній (фільтр по версії vs. реально не існує).

## Fallback policy (закріплено)

**Правило:** коли версія не визначена з explicit-параметра і не зрезолвлена з environment, каталог рахує її **останньою відомою (`latest known`)**. Жодних інших режимів — `unrestricted` (повернути все включно з deprecated), `error` (відмовитися виконувати запит), або «найстаріша» — НЕ реалізуємо.

**Що таке «остання відома».** Це **максимальна `since` серед усіх записів** поточного `ComponentRegistry.json`, обчислена composer-фазою у clio при білді. Зберігається в маніфесті каталогу як top-level поле:

```json
{
  "latestKnownVersion": "8.3.2",
  "components": [ … ],
  "categories": [ … ]
}
```

`latestKnownVersion` оновлюється автоматично при кожному композер-ранi. Hardcoded constants в C# коді — заборонено (інакше при додаванні версії в `creatio-ui` забудуть оновити сталу).

**Коли спрацьовує fallback (вичерпний перелік):**

| Тригер | Сценарій | `resolvedFrom` |
|---|---|---|
| Немає `environment-name` і немає `target-version` | `get-component-info` викликано «всуху» (новий workspace, дослідницький запит) | `latest-fallback` |
| `environment-name` задано, але cliogate < `2.0.0.32` | Старий стенд без `GetSysInfo` endpoint-у | `latest-fallback` |
| `environment-name` задано, але `GetSysInfo` повернув HTTP-помилку | Стенд недоступний, auth не пройшов, timeout | `latest-fallback` |
| `GetSysInfo` повернув `CoreVersion`, який не парситься як semver | Кастомний білд, dev-стенд із нестандартним рядком | `latest-fallback` |

**Що НЕ є fallback (важливо):**

- Якщо `target-version` передано explicit-параметром — він використовується **як є**, навіть коли версія старша за `latestKnownVersion - 2` або новіша за `latestKnownVersion`. AI/користувач відповідає за коректність. `resolvedFrom = "explicit"`.
- Якщо `target-version` явно вищий за `latestKnownVersion` — відповідь все одно йде, але з додатковим полем `warning: "target-version <X> exceeds latestKnownVersion <Y>; catalog may be incomplete for this version"`. Не помилка — інформаційний sign.

**Як AI має реагувати на `latest-fallback`:**

Guidance ([PageModificationGuidanceResource.cs](../clio/Command/McpServer/Resources/PageModificationGuidanceResource.cs)) має пояснювати:
- `resolvedFrom: "latest-fallback"` означає, що каталог показує **усе, що знає платформа в найновішій версії**, включно з компонентами/properties, які можуть бути відсутні на цільовому стенді.
- Якщо подальша операція (`update-page`) впала на не-існуючому property — це може бути legitimate сигнал, що `environment-name` варто передати у наступному виклику `get-component-info` для коректного фільтру.
- Не пропонувати користувачу запам'ятати `target-version` як substitute для `environment-name` — це делегує responsibility з ОС на AI.

**Чому `latest known`, а не альтернативи:**

- **vs `error`**: ламає UX для exploratory-запитів («покажи список доступних компонентів» без жодного env-у). Це поширений first-contact flow з MCP.
- **vs `unrestricted` (повернути все, включно з deprecated)**: видає AI компоненти, які платформа давно прибрала. Save впаде, але AI отримує contradictory signal — каталог сказав, що property `legacyStyleMode` існує, а API його відкинуло. Дебаг-нічна петля.
- **vs `oldest known`**: безпечніше за `latest` (нічого новенького AI не пропонує), але створює зворотну проблему — на 8.3-стенді AI не знатиме про properties, додані в 8.2. Заперечує сенс всієї фічі.

`latest known` із чітким маркером — це compromise між «дайте AI повний список» і «не брешіть AI про те, що property точно є». Маркер `latest-fallback` сигналізує АI: дані можуть бути супермножиною реального стенду.

## Архітектурні зміни на високому рівні

### 1. Розширити модель

- `ComponentRegistryEntry.Availability` → `AvailabilityRange? { Since, Until }` (nullable record).
- `ComponentPropertyDefinition.Availability` → той самий тип.
- `ComponentRegistry` стає або:
  - `{ "components": [...], "categories": [{"id":"containers","order":0,"label":"Containers"}] }` — обгортка-маніфест, або
  - окремий `Categories.json` (простіше для review).

### 2. Резолвер версії

- Новий сервіс `IPlatformVersionResolver` із кешем per `(environment-name, ttl)`.
- Реалізація через `IApplicationClient` → `/rest/CreatioApiGateway/GetSysInfo` → `SysInfo.CoreVersion`.
- Cliogate-version guard — soft-fail у fallback.

### 3. Filter pipeline у каталозі

- `IComponentInfoCatalog.Search(search, targetVersion)` — додатковий параметр.
- `IComponentInfoCatalog.Find(componentType, targetVersion)` — фільтрує properties до видимих для версії.
- Кеш ключовано по `(targetVersion, search)` поверх існуючого `Lazy<>` — дешево.

### 4. Розширити MCP контракт

```csharp
public sealed record ComponentInfoArgs(
    string? ComponentType = null,
    string? Search = null,
    string? EnvironmentName = null,   // → resolver
    string? TargetVersion = null      // override
);
```

`ComponentInfoResponse` додає `resolvedTargetVersion`, `resolvedFrom`.

### 5. Категорії — data-driven

Винести `CategoryOrder` із [ComponentInfoCatalog.cs:40](../clio/Command/McpServer/Tools/ComponentInfoCatalog.cs#L40) і [ComponentInfoTool.cs:17](../clio/Command/McpServer/Tools/ComponentInfoTool.cs#L17) у каталог. Це дозволить новим категоріям у нових версіях платформи з'являтися без code-changes.

### 6. Guidance оновлення

[PageModificationGuidanceResource.cs](../clio/Command/McpServer/Resources/PageModificationGuidanceResource.cs) повинен пояснювати AI:
- передавати `environment-name` у `get-component-info` для коректної фільтрації;
- читати `resolvedTargetVersion`, щоб не пропонувати компоненти/properties недоступні на цільовій версії.

## Відкриті питання для координації з командою

1. **Source of truth довгостроково.** Hand-curated forever vs. auto-extraction з платформи. **Окреме дослідження** — див. [05-source-of-truth-automation.md](05-source-of-truth-automation.md).
2. ~~**Fallback-семантика, коли версія не відома.**~~ **Закрито:** `latest known` із маркером `resolvedFrom: "latest-fallback"`. Деталі і edge-cases у секції [Fallback policy](#fallback-policy-закріплено).
3. **Версійна гранулярність.** `Major.Minor.Build` чи semver-стиль? Чи потрібен `pre-release` (`8.2.0-rc1`)?
4. **Migration plan для існуючих 92 записів.** Базова позиція — не чіпати (всі без `availability` = «завжди»). Питання, чи проставити explicit `since: "8.0.0"` у наступному релізі для clarity.
5. **`example` payload на компонент** теж може залежати від версії (нові props у прикладі). Чи робити мульти-`example` із `availability`, чи тримати один canonical приклад на «latest known»?
6. **Перевірки в CI.** Перевіряти, що жодна `since` не перевищує найбільшу зареєстровану версію (захист від типів).

## Що НЕ блокує і чого НЕ варто чіпати

- Існуючий `Lazy<>` залишається — додається вторинний фільтр-кеш per `(version, search)`.
- Fail-on-duplicate ([ComponentInfoCatalog.cs:101](../clio/Command/McpServer/Tools/ComponentInfoCatalog.cs#L101)) залишається — критично.
- Існуючі callers без `target-version` зберігають поточну поведінку — zero-break.
- **Не дробити** на per-version snapshot файли. На цьому масштабі один файл виграє по review-friendliness і entry barrier для контриб'ютора.

## Mandatory follow-up для реалізації

Згідно [clio/Command/McpServer/AGENTS.md](../clio/Command/McpServer/AGENTS.md):
- Зміни `get-component-info` потребують unit + E2E coverage у [clio.mcp.e2e](../clio.mcp.e2e).
- Резолвер версії — окремий unit suite (fake `IApplicationClient`).
- Guidance resources оновлюються в тій самій PR.

## Рекомендований порядок робіт

1. **Spike** — додати `availability` до моделі + 1–2 пілотні записи + filter API без MCP-параметра. Перевірити, що каталог продовжує завантажуватися.
2. **`IPlatformVersionResolver`** + cliogate guard + кеш. Окремий PR — реюзабельно поза каталогом.
3. **Розширення `ComponentInfoArgs`/`Response`** + інтеграція resolver-а.
4. **Категорії data-driven**.
5. **Backfill** `availability` для записів, де команда впевнена в `since`-версії (опціонально, можна відкласти).
6. **Guidance & docs**.
