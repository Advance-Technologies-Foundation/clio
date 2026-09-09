# Аудит доцільності e2e-рівня для `clio.mcp.e2e`

> **Статус:** аналітичний прохід. Код і тести **не змінювались** (жодного рефактора, жодного переносу).
> Це вхід для рішення команди, а не готова зміна.
>
> **Дата:** 2026-09-09 · **Гілка аналізу:** `claude/repo-update-z18z41` (base `master` @ `8d1e8cf3`)

---

## 1. Резюме

Головний висновок аудиту відрізняється від початкової гіпотези запиту.

**Гіпотеза була:** сюїта повільна, бо частина тестів марно вимагає розгорнутий Creatio.

**Що показали дані:** живий Creatio — **не** основна стаття витрат. Основна стаття — **226 запусків
дочірнього процесу `clio mcp-server`** у прогоні з 772 тестів (при задокументованому в репозиторії
старті ≈10 с це ~38 хв послідовно / ~19 хв при `NumberOfTestWorkers=2`). Тобто **старт процесу
поглинає близько половини заявлених 44m 20s**, і він однаковий для тесту, що звертається до стенду,
і для тесту, що лише перевіряє формат помилки.

З цього випливає порядок дій, зворотний до очікуваного:

| # | Дія | Тестів торкається | Орієнтовний виграш | Ризик |
|---|---|---|---|---|
| 1 | Розділити один сервер на фікстуру там, де конфіг **не** відрізняється між тестами | 0 (рівень не змінюється) | **~8.7 хв** | мінімальний |
| 2 | Понизити 32 контрактні тести, що живуть у фікстурах зі стартом-на-тест | 32 | **~5.3 хв** | низький |
| 3 | Перетегувати 29 хибно-`Sandbox` тестів у `NoEnvironment` | 29 | стендний час, не wall-clock | низький |
| 4 | Згорнути 93 тести «інструмент видно у tools/list» у 2–3 агрегатні | ~90 | ~1–1.4 хв | середній |
| 5 | Проріділи 77 тестів «невалідне середовище» до ~10 представників | ~65 | ~2.5–3.2 хв | середній |
| 6 | Винести 71 суто-unit тест із `clio.mcp.e2e` до `clio.tests` | 71 | ~0 хв, але **вони отримують PR-гейт** | середній (треба рухати harness) |

Сумарно: **~18–20 хв із 44m 20s** (≈40–45 %), з них лише ~9 хв дає власне пониження рівня —
решту дає усунення надлишкових стартів процесу.

Окремий, не-часовий висновок, який може виявитись важливішим за секунди:
**`clio.mcp.e2e` не запускається жодним GitHub-lane** (`docs/knowledge/Tests/no-github-lane-runs-the-mcp-e2e-project.md`).
Тобто 71 тест, що вже сьогодні є unit-тестами і лежить у цьому проєкті, **не блокує жодного PR**.
Це аргумент за перенесення сильніший за будь-яку економію часу.

---

## 2. Метод і чесні межі аналізу

### Що зроблено
1. Механічний інвентар усіх 239 `.cs` (68 323 рядки): підрахунок `[Test]`/`[TestCase]`, категорій,
   способу старту сервера, сигналів пісочниці зі списку `spec/mcp-e2e-tiering/mcp-e2e-tiering-spec.md`.
2. Ручне читання 14 фікстур (усі пріоритетні з Кроку 3 запиту + найважчі за стартами).
3. Перехресна перевірка дублювання проти `clio.tests` (12 333 unit-тести) — за іменами фікстур і за
   текстом асертів (`grep` по повідомленнях помилок продукту).
4. Читання наявних артефактів: `spec/mcp-e2e-tiering/*`, `clio.mcp.e2e/AGENTS.md`,
   `docs/knowledge/Tests/*`, `clio/Command/McpServer/AGENTS.md`.

### Чого **не** зроблено — і це впливає на числа
- **Часи не виміряні.** У контейнері немає .NET SDK (`dotnet: command not found`), сюїту запустити
  неможливо. Усі оцінки часу — модельні, з двох джерел у самому репозиторії:
  - `Support/Mcp/McpContractFixtureBase.cs` (рядок 10-11): *«eliminating the per-test ~10 s startup overhead»* → **S ≈ 10 с** на старт сервера;
  - `ReadResponseDeadlineToolE2ETests.cs` (рядок 58-61): spawn + handshake **p50 2.763 с** на Windows Server 2022 (ADR §2.4) — це легший *worker*-режим, не повний сервер.
  Нижче наведено діапазон **S = 8…12 с**. Числа з запиту (44m 20s, 1m 42s, 30s–1m 35s) взято як вхідні.
- Оцінки часу окремих викликів інструмента (round-trip після старту) прийнято 0.2–1.0 с; це
  припущення, не вимір.

### Звірка кількості тестів
| Набір | Тестів |
|---|---|
| Усього `[Test]` + `[TestCase]` у проєкті | **924** (178 фікстур) |
| мінус категорія `McpE2E.ProcessDesigner` (виключена фільтром TeamCity) | −112 |
| мінус `McpE2E.Manual` / `LocalOnly` + `[Explicit]` | −40 |
| **Реально біжить у TeamCity** | **772** |

772 ≈ 789 із запиту — розбіжність у ~17 пояснюється іншим комітом або розгорткою `[TestCase]`.
Тобто аналізований набір збігається з тим, який дав 44m 20s.

### Структура сюїти (виміряно статично)
| Показник | Значення |
|---|---|
| Тестів усього / у прогоні | 924 / 772 |
| Фікстур | 178 |
| `[NonParallelizable]` фікстур | **136** |
| `[Parallelizable(ParallelScope.Self)]` фікстур | 26 |
| `NumberOfTestWorkers` | **2** |
| Стартів `clio mcp-server` (усі) | **366** |
| Стартів `clio mcp-server` (набір 772) | **226** |
| Фікстур зі спільним сервером (`McpContractFixtureBase`) | 100 |
| Фікстур, що стартують сервер **на кожен тест** | 54 |
| Фікстур, що сервер **не** стартують узагалі | 24 (112 тестів) |
| Тестів у тирі `McpE2E.NoEnvironment` | ~411 + частина змішаних |
| Тестів у тирі `McpE2E.Sandbox` | ~219 + частина змішаних |

---

## 3. Куди йде час: три статті витрат

```
44m 20s ≈  [ старт 226 процесів clio mcp-server ]  +  [ робота проти стенду ]  +  [ round-trip'и ]
              ~19 хв @ S=10с, 2 воркери                   решта                      хвилини
           ▲ не залежить від тиру тесту          ▲ ось це і є "справедливий e2e"
```

Наслідок, який визначає всі рекомендації нижче: **прибрати тест із фікстури зі спільним сервером
економить ~0.2–1 с; прибрати тест із фікстури зі стартом-на-тест економить ~10 с.** Один і той самий
тест коштує в 10–50 разів по-різному залежно від того, де він лежить. Тому список кандидатів
відсортовано не за «чистотою логіки», а за фактичною ціною.

### 3.1 Найдорожчі фікстури за стартами

| Фікстура | Тестів | Стартів | Конфіг різний між тестами? | Коментар |
|---|---:|---:|---|---|
| `ModifyBusinessProcessToolE2ETests` | 56 | **56** | **ні** (`ArrangeAsync` будує ідентичні `settings`) | у лейні ProcessDesigner (виключена з TeamCity), але ~9 хв локально |
| `CreateBusinessProcessToolE2ETests` | 26 | **26** | **ні** | те саме |
| `ApplicationToolE2ETests` | 16 | **16** | **ні** | ~2.5 хв чистого старту |
| `ApplicationSectionToolE2ETests` | 14 | **14** | **так** — кожен тест пише свій `appsettings.json` | старт неусувний без зміни дизайну |
| `DataForgeToolE2ETests` | 9 | 9 | ні | ~1.3 хв |
| `CreateRelatedPageAddonToolE2ETests` | 7 | 7 | ні | ~1 хв |
| `ApplicationSectionUpdateToolE2ETests` | 5 | 5 | так (4 з 5) | |
| `LinkFromRepositoryToolE2ETests` | 5 | 5 | ні | |
| `ApplicationSectionMaintenanceToolE2ETests` / `DbTemplatePruneToolE2ETests` / `GetRelatedPageAddonToolE2ETests` / `CreateWorkspaceToolE2ETests` / `WorkspaceSyncToolE2ETests` | 4 кожна | 4 | ні (крім `CreateWorkspace`) | |

**52 старти** у наборі 772 припадають на фікстури **без** будь-якої пер-тестової конфігурації —
вони технічно можуть перейти на `McpContractFixtureBase` без зміни жодного асерту. Це **~8.7 хв**
(S=10 с) і **найдешевша зміна в усьому звіті**: рівень тесту не змінюється, покриття не змінюється.

---

## 4. Крок 3 — пріоритетні класи (детально)

| Тест / клас | Поточний рівень | Що реально робить | Рекомендований рівень | Обґрунтування | Виграш часу |
|---|---|---|---|---|---|
| **`FlatArgsProgressTokenE2ETests`** (1 тест, 1m 42s) | E2E · `NoEnvironment` | Реальний `clio mcp-server` + TCP-listener на loopback, що приймає з'єднання і **не відповідає**. Перевіряє, що `ProgressToken` переживає in-place перезапис `Arguments` фільтром, доїжджає до relay у worker-процес і що звідти течуть `notifications/progress`. | **Лишається E2E** | Очікування **не** через `sleep`: `WaitForCapturedProgressAsync` керується нотифікаціями і виходить за умовою `Count > 0`. 1m 42s — це не polling, а сума: старт сервера + `ReadDeadlineSeconds=5` + spawn/kill worker'а. Unit-тест T8 (згаданий у самому коментарі) вже покриває мутацію `Arguments`; те, що тут — **relay між процесами** — in-process не відтворюється. | 0 (не чіпати) |
| — *можлива оптимізація* | | | | `ReadDeadlineSeconds=5` і `HeartbeatIntervalSeconds=0.2` вже мінімальні; 3-хвилинний `ArrangeContext` — стеля, не витрата. Реальний резерв — тільки старт сервера. | ~0 |
| **`ApplicationSectionToolE2ETests`** — 3 тести: `Should_Reject_Missing_ApplicationCode`, `Should_Reject_Missing_Caption`, `Should_Reject_Localization_Map_Fields` | E2E · **`Sandbox`** | `ResolveReachableEnvironmentAsync` (потрібен стенд) → старт сервера → виклик `create-app-section` без обов'язкового поля. Валідація спрацьовує в `ApplicationTool.ValidateSectionCreateArgs` (рядки 409–425) **до** будь-якого звернення до середовища. | **Unit** (видалити з e2e) | Стенд у цих тестах не бере участі в перевірці взагалі. Гірше — **вони вже продубльовані**: `clio.tests/Command/McpServer/ApplicationToolTests.cs` рядки 468, 706 асертять ті самі рядки (`*application-code is required*`, `*scalar-only*`) і додатково доводять `DidNotReceiveWithAnyArgs()` на сервісі, чого e2e-версія не робить. | **~30 с** (3 старти) + стендний час |
| `ApplicationSectionToolE2ETests` — `Create_WithCustomEntity/WithPlatformEntity_Should_Return_Structured_Readback_Data`, `WithNonExistentEntity`, `ConcurrentCallsAgainstOneApp` | E2E · `Sandbox` | Реальне створення секції + read-back із БД, конкурентні виклики | **Лишається E2E** | Це і є справедливий e2e: запис + читання назад + конкурентність. Unit цього не ловить. | 0 |
| `ApplicationSectionToolE2ETests` — `Should_Return_Transport_Classified_Error_For_Unreachable_Environment`, `Should_Return_InProgress_When_Response_Deadline_Elapses`, `Should_Not_Hang_When_ElicitationCapableClient_Never_Answers` | E2E · `NoEnvironment` | Свій `appsettings.json` + loopback-заглушка на кожен тест | **Лишається E2E** | Кожен тест **мусить** мати свій конфіг у дочірньому процесі → спільний сервер неможливий. Тут старт-на-тест виправданий. | 0 |
| **`SchemaSyncToolE2ETests`** — 15 із 25 тестів (`*_Before_Environment_Resolution`, `*_Reject_*_With_Rename_Hint`, `Should_Reject_Unknown_Operation_Field`, `Should_Reject_Blank_SchemaName`, `Should_Still_Bind_Legacy_Operation_Key`, …) | E2E · **`Sandbox`** (клас-левел) | `ArrangeAsync(requireEnvironment: false)` + випадкове неіснуюче ім'я середовища. Чиста перевірка форми аргументів і повідомлень. | **`NoEnvironment`** негайно; більшість — **Unit** | Фікстура має один клас-левел `[Category("McpE2E.Sandbox")]`, хоча **15 із 26 arrange-викликів** явно `requireEnvironment: false`. Наслідок подвійно поганий: у швидкому лейні вони скіпаються (порушує ціль `Skipped == 0` зі специфікації тиру), у стендовому — займають час стенду ні за що. | старт спільний → wall-clock ~0; **звільняє стенд**, вмикає 15 тестів у швидкий гейт |
| `SchemaSyncToolE2ETests` — `SchemaSync_AbsentSchema_*`, `ExistingSchema_*`, `IdenticalReplay_*`, `CrossPackageSchema_*`, `CreateEntity_Should_Create_Virtual_Schema_Without_Physical_Table` | E2E · `Sandbox` | Реальні create/reconcile/replay + `PostgresTableProbe` проти БД | **Лишається E2E** | Ідемпотентність повторного прогону і відсутність фізичної таблиці перевіряються тільки проти живої БД. | 0 |
| **`EntitySchemaToolE2ETests`** — 7 тестів `*_Should_Report_Invalid_Environment` | E2E · `NoEnvironment` | Виклик із неіснуючим середовищем, асерт структурованої помилки | **Unit** (лишити 1 представника на весь pipeline) | Помилка формується спільною інфраструктурою (`ToolCommandResolver` / `BaseTool` / `SessionTargetNormalizer` → `EnvironmentResolutionException`), а не кожним інструментом. У `clio.tests` **30 файлів** уже асертять цей самий тип. Сім копій на одну фікстуру — це тест на `BaseTool`, помножений на 7. | ~2–7 с |
| `EntitySchemaToolE2ETests` — `CreateEntitySchema_Should_Reject_NonLatin_Text_Under_EnUs_Title` | E2E · `NoEnvironment` | Guard скрипту/культури | **Unit — уже дубль** | `clio.tests/Command/CaptionCultureScriptGuardTests.cs` — **42 тести**, рядок 21: *«throws when Cyrillic text is stored under the Latin-script en-US key»*. Це той самий кейс. | ~1 с |
| `EntitySchemaToolE2ETests` — `ModifyEntitySchemaColumn_Should_Reject_Unsupported_Sequence_Mask` | E2E · **`Sandbox`** | `ArrangeSharedSchemaAsync()` — реальна схема на стенді — щоб перевірити відмову на масці `LN-{0}-END` | **Unit — уже дубль** | `clio.tests/Command/EntitySchemaDesignerSupportTests.cs` рядок 373: *«A Sequence mask with a suffix or repeated placeholder is rejected»*. Схема на стенді для цього не потрібна: guard спрацьовує до save. | стендний час |
| `EntitySchemaToolE2ETests` — `ModifyEntitySchemaColumn_Should_Reject_LookupConstDefault_When_RecordMissing` | E2E · `Sandbox` | Перевіряє відсутність запису в **referenced schema** | **Лишається E2E** | Потрібне реальне читання БД: «запису немає» — факт стану, не форми. | 0 |
| `EntitySchemaToolE2ETests` — 19 тестів create/modify/read-back (`Color` column, `Money` alias, `DefaultValueConfig`, `UsageType`, `BinaryLike`, `ImageLookup`, …) | E2E · `Sandbox` | Запис → публікація → читання назад через MCP | **Лишається E2E** | Класичний справедливий e2e: перевіряється, що платформа зберегла і повернула типи так, як заявлено. | 0 |
| **`McpWorkerContainmentE2ETests`** (3) | E2E · `NoEnvironment` | Породжує **реальні ОС-процеси** трьома поколіннями, вбиває батька, асертить **зникнення pid'ів** | **Лишається E2E** | Гіпотеза «тестується без живого Creatio» — правильна, і так уже є: Creatio тут не потрібен. Але й unit неможливий: перевіряється `setpgid` + parent-death signalling / job-object. Polling є (`Task.Delay(50/100)`), але це єдиний портабельний спосіб спостерігати «процесу більше немає», і цикл виходить достроково. | 0 |
| **`McpWorkerWedgeE2ETests`** (2, 807 рядків) | E2E · `NoEnvironment` | Реальний сервер + **детермінований стаб Creatio** (`CreatioWedgeStubServer`). Асертить **лічильники backend-запитів**, не час. | **Лишається E2E** (де-факто це вже integration із мок-бекендом) | Живий Creatio не потрібен — стаб. Сигнатура дефекту («виклик повернувся по дедлайну, так і не зробивши HTTP-запит») видима лише через дельту лічильника стабу; таймінговий асерт її не бачить. Ідеальний зразок того, як демотувати інші Sandbox-тести. | 0 |
| **`McpWorkerCohortParityE2ETests`** (1, 371 рядок) | E2E · `NoEnvironment` | Порівнює відповідь cohort-інструмента через worker-процес і in-process, обидва проти стабу | **Лишається E2E** | In-process «плече» отримується не DI-підміною, а реальним `--worker` дочірнім процесом (recursion guard). Відтворити це в unit неможливо за побудовою. | 0 |
| **`ReadResponseDeadlineToolE2ETests`** (3) | E2E · `NoEnvironment` | Loopback-стенд, що не відповідає, + `CLIO_MCP_WORKER_BUDGET_SECONDS=8` / `CLIO_MCP_READ_DEADLINE_SECONDS=1` | **Лишається E2E** | Гіпотеза «арифметика бюджету → unit із фейковим годинником» **не підтвердилась**: арифметика тут не перевіряється. Перевіряється, що два різні механізми (батько **вбиває** дитину vs in-process **покидає** роботу) дають розрізнювані конверти (`worker-budget-expired` vs `read-response-timed-out`). Арифметику вже покривають `McpReadResponseDeadlineTests` (23 тести) і `McpWorkerCallDispatcherTests` (39). | 0 |
| **`PageValidateToolE2ETests`** (34) | E2E · `NoEnvironment`, спільний сервер | 33 із 34 — валідація тіла сторінки (AMD/mobile JSON) без будь-якого середовища | **Unit** — лишити 1–2 наскрізні | Перекриття з unit доведене поіменно: e2e `Reject_Mobile_Body_With_Validators` ↔ unit `ValidatePageToolTests` («invalid for a mobile JSON body that contains a 'validators' section»); `Reject_Undeclared_Handler_Helper` ↔ unit («handler calls an undeclared module-scope helper»); `Reject_Mobile_Body_That_Trips_The_Differ` ↔ unit («differ oracle surfaces a not-a-container error»); `Accept_Explicit_Version_Argument` ↔ unit («scopes chart-widget validation to the explicit version argument»). Поруч лежать `MobileDiffApplyValidatorTests` (21) і `PageBodySyntaxValidatorTests` (15). | ~10–30 с (спільний сервер → дешеві) |
| **`ToolContractGetToolE2ETests`** (26) | E2E · `NoEnvironment`, спільний сервер | Асертить вміст контрактів (поля, підказки, canonical-імена) | **переважно Unit** | `clio.tests/.../ToolContractGetToolTests.cs` — **123 unit-тести** на той самий об'єкт, включно з універсальним оракулом (рядок 165): *«Every hidden, clio-run-invokable tool resolves to a contract entry with a schema»*. E2E-цінність тут — довести, що індекс віддається справжнім процесом; для цього досить 1–2 тестів. | ~5–20 с |

---

## 5. Крок 1–2 — класифікація всієї сюїти за патернами

Патерни визначені за іменами тестів (962 просканованих сигнатур) і перевірені читанням вибірки.

| Патерн | Тестів (набір 772) | З них у фікстурах зі стартом-на-тест | Що перевіряє | Рекомендація |
|---|---:|---:|---|---|
| **A. «інструмент видно у tools/list / lazy surface»** | **93** | 4 | одна й та сама інваріанта на 93 інструменти | **1–2 агрегатні E2E** + наявний unit-оракул `McpProfileGatingTests` (реєструє примітиви в реальний `IMcpServerBuilder`, серіалізує payload, тримає бюджети на кількість і байти) та `ToolContractGetToolTests` (рядок 165) |
| **B. «невалідне / недосяжне середовище»** | **77** | 13 | структурована помилка спільного pipeline | лишити **~10** представників (по одному на клас відповіді), решту — Unit |
| **C. «reject / validation failure / refuse»** | **113** | 15 | guard clauses до звернення до середовища | **Unit**, крім тих, де guard перевіряє стан на сервері |
| **D. «rename hint / alias»** | **17** | 0 | нормалізація імен аргументів | **Unit** (`McpToolArgumentSupport.EnvironmentNameAliases`) — крім 1–2 наскрізних |
| **E. «args wrapper / flat shape»** | 20 | — | нормалізація flat↔wrapped на транспорті | **Лишається E2E** — `McpToolErrorFilterE2ETests` доводить поведінку на реальному stdio; unit не бачить дроту |
| **F. Реальні read-back / конкурентність / БД** | ~180 (Sandbox) | — | запис і читання назад у Creatio | **Лишається E2E** |
| **G. ОС-процеси, worker-containment, shutdown, raw stdio** | ~15 | — | між-процесна поведінка | **Лишається E2E** |
| **H. Чиста логіка без сервера і без стенду** | **112** (24 фікстури) | 0 | in-memory | **Unit → `clio.tests`** (див. 5.1) |

### 5.1 Фікстури, що **ніколи** не стартують MCP-сервер і не торкаються стенду

Це тести, які вже сьогодні є unit-тестами — але лежать у проєкті без PR-гейту.

| Фікстура | Тестів | Категорія сьогодні | Що тестує |
|---|---:|---|---|
| `UninstallWarningIisApplicationPoolResolverE2ETests` | **18** | `McpE2E.NoEnvironment` | парсинг XML `appcmd` + `ClioEnvironmentCommandResolver` — чиста строкова логіка |
| `TransientPlatformConditionRetryGateTests` | 14 | `Unit` | retry-gate |
| `DataForgeReadinessGateTests` | 13 | `Unit` | readiness-gate |
| `BoundedPollGateTests` | 5 | `Unit` | bounded poll |
| `WorkerSpawnObserverReleaseWaitTests` | 5 | `NoEnvironment` | wait-логіка спостерігача |
| `FixtureCleanupOwnershipTests` | 5 | `NoEnvironment` | інваріанта власності на прибирання |
| `MessageCollectingProgressWaitTests` | 4 | `NoEnvironment` | збір повідомлень прогресу |
| `ClioCliCommandRunnerRedactionTests` | 3 | `NoEnvironment` | редакція секретів |
| `RedisSandboxClientTests` | 2 | `Unit` | `BuildConfigurationOptions` — in-memory, без Redis |
| `ClioCliCommandRunnerEnvelopeTests` | 2 | `NoEnvironment` | розбір конверта |
| **Разом** | **71** | | |

**Застереження до перенесення.** Це тести **харнеса**, а не продукту: `IisApplicationPoolResolver`,
`BoundedPollGate`, `TransientPlatformConditionRetryGate`, `DataForgeReadinessGate`,
`RedisSandboxClient`, `MessageCollectingProgress`, `WorkerSpawnObserver` живуть у
`clio.mcp.e2e/Support/`. Перенести їхні тести в `clio.tests` = або винести харнес у спільну
support-збірку, або лишити як є і свідомо прийняти, що вони не гейтять merge. Це рішення про
структуру рішень, не про тести — тому воно окремим пунктом, а не «просто перекласти файли».

### 5.2 Хибно позначені `Sandbox` (arrange не потребує середовища)

| Фікстура | Тестів усього | З них env-free arrange | Тегування |
|---|---:|---:|---|
| `SchemaSyncToolE2ETests` | 25 | **15** | клас-левел `Sandbox` |
| `DataBindingDbToolE2ETests` | 12 | **8** | клас-левел `Sandbox` |
| `DataBindingToolE2ETests` | 3 | **3** | клас-левел `Sandbox` |
| `DataForgeToolE2ETests` | 9 | 1 | клас-левел `Sandbox` |
| `ClearBrowserSessionToolE2ETests` | 2 | 1 | клас-левел `Sandbox` |
| `GetBrowserSessionToolE2ETests` | 2 | 1 | клас-левел `Sandbox` |
| **Разом** | | **29** | |

29 тестів мають `McpE2E.Sandbox`, хоча їхній arrange явно `requireEnvironment: false` /
`ArrangeInvalidEnvironment`. Це прямо суперечить приймальному критерію `Skipped == 0` для тиру
`NoEnvironment` зі `spec/mcp-e2e-tiering/mcp-e2e-tiering-spec.md`: у швидкому гейті вони не
запускаються, у стендовому — не додають нічого. Виправлення — пер-методне `[Category]`, як уже
зроблено в `EntitySchemaToolE2ETests`.

---

## 6. Дублювання покриття — доведені випадки

Перекриття шукалось не за назвами, а за **текстом асертів проти повідомлень продукту**.

| E2E-тест | Unit-двійник | Доказ |
|---|---|---|
| `ApplicationSectionCreate_Should_Reject_Missing_ApplicationCode` (Sandbox) | `ApplicationToolTests.ApplicationSectionCreate_Should_Return_Error_When_ApplicationCode_Is_Missing` | обидва матчать `*application-code is required*`; unit ще й асертить `DidNotReceiveWithAnyArgs()` |
| `ApplicationSectionCreate_Should_Reject_Localization_Map_Fields` (Sandbox) | `ApplicationToolTests` (рядок 706) | обидва матчать `*scalar-only*` |
| `CreateEntitySchema_Should_Reject_NonLatin_Text_Under_EnUs_Title` | `CaptionCultureScriptGuardTests` (42 тести) | той самий guard `EnsureCaptionMatchesCulture` |
| `ModifyEntitySchemaColumn_Should_Reject_Unsupported_Sequence_Mask` (Sandbox!) | `EntitySchemaDesignerSupportTests` (рядок 373) | «mask with a suffix or repeated placeholder is rejected» |
| `PageValidateTool_Should_Reject_Mobile_Body_With_Validators` | `ValidatePageToolTests` (рядок 60) | дослівно той самий кейс |
| `PageValidateTool_Should_Reject_Undeclared_Handler_Helper` | `ValidatePageToolTests` (рядок 276) | той самий кейс |
| `PageValidateTool_Should_Reject_Mobile_Body_That_Trips_The_Differ` | `ValidatePageToolTests` (рядок 223) + `MobileDiffApplyValidatorTests` (21) | той самий оракул differ'а |
| 93 × `*_Should_Be_Listed/Discoverable_*` | `McpProfileGatingTests` (7 тестів, бюджет на кількість і байти) + `ToolContractGetToolTests` (рядок 165, «every hidden tool resolves to a contract entry») | unit-оракул **універсальний**, e2e-версії — поштучні |
| 77 × `*_Should_Report_Invalid_Environment` | 30 файлів у `clio.tests`, що асертять `EnvironmentResolutionException` | помилку формує спільний `ToolCommandResolver`/`BaseTool`, не інструмент |

Груба метрика зверху: **99 із 168** e2e-фікстур мають однойменного unit-двійника в `clio.tests`
(59 %). Це не доказ дублювання саме по собі — але разом із таблицею вище показує, що шар unit тут
не «тонкий», і e2e часто повторює його з іншого боку дроту.

---

## 7. Ризики: що unit **не** зловить (і має лишитись e2e)

Це відповідь на прямий пункт запиту — де пониження рівня втратить сигнал.

1. **Серіалізація/біндинг через реальний MCP-SDK.** Unit-тест викликає метод інструмента з уже
   типізованим record'ом. Він не бачить, чи JSON із дроту взагалі зв'яжеться. Саме це ловить
   `McpToolErrorFilterE2ETests` (flat vs wrapped vs hybrid vs case-collision) — цю фікстуру
   понижувати не можна.
2. **Прогрес-канал і `_meta`.** `AGENTS.md` MCP-директорії прямо попереджає: побудова нового
   `CallToolRequestParams` замість мутації `Arguments` **мовчки** ламає `notifications/progress` і
   `_meta.clioStageEvent`, який споживає ClioRing. In-process тест цього не бачить.
   → `FlatArgsProgressTokenE2ETests`, `DeployUninstallProgressTests`, `WorkerProgressStreaming`.
3. **Межа процесу.** Бюджет worker'а реалізований як **вбивство дитини батьком**; read-deadline —
   як **покидання роботи** в тому ж процесі. Конверти навмисне ділять токен `error-class=creatio-timeout`
   і різняться лише маркером. Тест, що приймав би будь-який маркер, перестав би ловити тихий
   відкат cohort-інструмента на in-process шлях. → `ReadResponseDeadlineToolE2ETests`,
   `McpWorkerCohortParityE2ETests`.
4. **Витік дескрипторів / осиротілі процеси.** Перевіряється лише існуванням pid'а після kill.
   → `McpWorkerContainmentE2ETests`.
5. **stderr як канал діагностики.** In-process тест ніколи не в режимі MCP-сервера, тому прапорець
   транспорту, rate-gate і сам запис були запінені окремо, а проводка між ними — ні. Шов закриває
   `StdioClientTransportOptions.StandardErrorLines` в e2e.
6. **Реальний OData/DataService-шар і асинхронна перебудова.** `create-entity-schema` повертає
   керування, щойно **запустив** глобальну асинхронну перебудову OData, яка переживає команду
   (`docs/knowledge/Tests/the-parallel-pool-cannot-disturb-the-shared-stand.md`). Це поведінка, яку
   не відтворює жоден мок.
7. **Конкурентність проти одного застосунку.** `ApplicationSectionCreate_ConcurrentCallsAgainstOneApp`,
   `ClioPagesConcurrencyE2ETests` — перевіряють per-key серіалізацію на реальному стенді.
8. **Сумісність із ClioRing.** `AGENTS.md` вимагає consumer-driven перевірок; частина цих інваріант
   (ordered replay, sequence-zero manifest, terminal semantics) живе саме в e2e.

**Окремий ризик самої ідеї «замокати транспорт».** У сюїті вже є 5 стабів
(`CreatioWedgeStubServer`, `RuntimeDetectionStubServer`, `SqlSchemaDesignerStubServer`,
`ODataPreWriteStand`, `McpHttpPassthroughStand`), і вони працюють. Але кожен новий стаб — це другий
опис контракту платформи, який тихо розходиться з реальним. Стаб виправданий там, де асерт іде на
**лічильник запитів** (wedge), і сумнівний там, де асерт іде на **вміст відповіді Creatio**.

---

## 8. Топ-5 за ROI

| # | Дія | Тестів | Виграш (S=8…12 с) | Складність |
|---|---|---:|---|---|
| 1 | `ApplicationToolE2ETests` (16), `DataForgeToolE2ETests` (9), `CreateRelatedPageAddonToolE2ETests` (7), `LinkFromRepositoryToolE2ETests` (5), `GetRelatedPageAddonToolE2ETests` (4), `ApplicationSectionMaintenanceToolE2ETests` (4), `DbTemplatePruneToolE2ETests` (4), `WorkspaceSyncToolE2ETests` (4) → **спільний сервер на фікстуру** | 0 понижено | **7…10.4 хв** | низька: конфіг між тестами не відрізняється |
| 2 | 32 контрактні тести у фікстурах зі стартом-на-тест → Unit (список у §9) | 32 | **4.3…6.4 хв** | низька; 5 із них уже дублі |
| 3 | 93 «tool is listed» → 2 агрегатні e2e-снапшоти + опора на `McpProfileGatingTests` | ~90 | 1…1.4 хв + мінус ~90 підтримуваних тестів | середня: домовитись про формат снапшоту |
| 4 | 77 «invalid environment» → ~10 представників | ~65 | 2.5…3.2 хв | середня: треба вибрати представників за класами помилки |
| 5 | `ModifyBusinessProcessToolE2ETests` (56) + `CreateBusinessProcessToolE2ETests` (26) → спільний сервер | 0 | **11…16 хв локально** (лейн ProcessDesigner виключено з TeamCity) | низька |

Пункт 5 не впливає на 44m 20s, але це найбільший одиничний резерв для розробника, який запускає
process-designer лейн вручну — а `McpE2ECategories.ProcessDesigner` документує, що це ~59+ тестів,
які інакше ніколи не біжать.

---

## 9. Іменний список кандидатів на пониження (найдорожчі — у фікстурах зі стартом-на-тест)

Кожен рядок = один старт `clio mcp-server` (~10 с).

**«reject / validation» (15):**
`ApplicationSectionToolE2ETests`: `Should_Reject_Missing_ApplicationCode`, `Should_Reject_Missing_Caption`, `Should_Reject_Localization_Map_Fields` ·
`ApplicationSectionUpdateToolE2ETests`: `Should_Reject_Request_Without_Mutable_Fields`, `Should_Reject_Missing_ApplicationCode`, `Should_Reject_Missing_SectionCode`, `Should_Reject_Localization_Map_Fields` ·
`ApplicationSectionMaintenanceToolE2ETests`: `ApplicationSectionGetList_Should_Reject_Missing_ApplicationCode`, `ApplicationSectionDelete_Should_Reject_Missing_SectionCode` ·
`ApplicationToolE2ETests`: `ApplicationGetInfo_Should_Reject_Missing_Identifiers`, `ApplicationGetInfo_Should_Reject_Both_Identifiers`, `ApplicationCreate_Should_Reject_Localization_Map_Fields`, `ApplicationCreate_Should_Reject_Malformed_OptionalTemplateDataJson` ·
`CreateRelatedPageAddonToolE2ETests`: `Should_Reject_Null_Pages_Entry` ·
`DeployIdentityToolE2ETests`: `Should_Reject_Obsolete_EnvironmentName_Field`

**«invalid environment» (13):**
`ApplicationSectionToolE2ETests.Should_Return_Transport_Classified_Error_For_Unreachable_Environment` (*лишити — свій конфіг*) ·
`ApplicationToolE2ETests`: `ApplicationCreate_Should_Report_Invalid_Environment_Failure`, `ApplicationCreate_Should_Report_Invalid_Template_Failure` ·
`CreateRelatedPageAddonToolE2ETests`: 4 × `*_Should_Bind_*_And_Report_Invalid_Environment` ·
`GetRelatedPageAddonToolE2ETests`: 2 × ·
`FindAppToolE2ETests.Should_Report_Invalid_Environment_With_Actionable_Hint` ·
`InstallApplicationToolE2ETests.Should_Report_Invalid_Environment_Failure` ·
`RemovePackageDependencyToolE2ETests.Should_Report_Invalid_Environment_Failure` ·
`WatchCompilationToolE2ETests.Should_Report_Invalid_Environment_Failure`

> Нюанс по 4 тестах `CreateRelatedPageAddon_Should_Bind_*_And_Report_Invalid_Environment`: назва
> обіцяє два різні асерти. Частина «Report_Invalid_Environment» — дубль pipeline'у; частина
> «Should_Bind_Pages_Payload» — реальний біндинг складного payload'а через дріт, і **це** варто
> зберегти (можливо, одним тестом на всі чотири форми payload'а замість чотирьох стартів).

**«advertise» (4):** `CreateRelatedPageAddon_Should_Be_Discoverable_On_Lazy_Surface`,
`GetRelatedPageAddon_Should_Be_Discoverable_On_Lazy_Surface`,
`RemovePackageDependency_Should_Be_Discoverable_On_Lazy_Surface`,
`WatchCompilation_Should_Be_Advertised_By_Mcp_Server`

---

## 10. Рекомендований порядок (щоб не зламати гейт)

1. **Спершу структура, потім рівні.** Пункти 1 і 5 із §8 не змінюють жодного асерту — їх можна
   зробити окремим PR і одразу отримати ~9 хв. Це також зробить наступні кроки дешевшими для
   вимірювання.
2. **Потім тегування** (§5.2): 29 тестів → `NoEnvironment`. Перевірка успіху — прогін
   `--filter "Category=McpE2E.NoEnvironment"` без стенду має дати `Skipped == 0` (критерій зі
   `mcp-e2e-tiering-spec.md`).
3. **Потім пониження** — по одній групі за раз, і **лише після** того, як unit-двійник існує і
   червоніє на зламаній логіці. Для 8 позицій із §6 двійник уже є; для решти його треба спершу
   написати.
4. **Виміряти.** Жодне число цього звіту не є виміром. Перед і після кожного кроку варто зняти
   `--logger "trx"` і порівняти `Duration` по фікстурах — це дасть реальний S замість ~10 с
   із коментаря.
5. **Рішення про 71 unit-тест харнеса** (§5.1) — окремо, бо воно про структуру проєктів, а не про
   тести.

---

## 11. Обмеження цього аудиту

- Часи **модельні**, не виміряні (немає .NET SDK у середовищі аналізу). Реальний S може відрізнятись
  у 1.5–2 рази, і тоді всі оцінки виграшу масштабуються лінійно.
- Патерни A–E визначені за **іменами** тестів; вибірка перевірена читанням, але окремі тести можуть
  бути класифіковані невірно. Кожен рядок §9 треба прочитати перед зміною.
- Дублювання доведене для 8 груп; повний перебір 924 × 12 333 не робився.
- Аудит не оцінював `clio-ring` та вимоги ClioRing-сумісності до конкретних тестів — за `AGENTS.md`
  будь-яке пониження тесту, що покриває споживаний Ring контракт, потребує окремої перевірки
  сумісності.

---

## Додаток: команди відтворення інвентарю

```bash
# кількість тестів і категорій по фікстурах
grep -rc "^\s*\[Test" clio.mcp.e2e --include=*.cs

# фікстури, що стартують сервер на кожен тест
grep -rln "McpServerSession.StartAsync" clio.mcp.e2e --include=*.cs \
  | xargs grep -L "McpContractFixtureBase"

# хибно позначені Sandbox
grep -rn "requireEnvironment: false\|ArrangeInvalidEnvironment" clio.mcp.e2e --include=*.cs

# швидкий гейт (має дати Skipped == 0)
dotnet test clio.mcp.e2e/clio.mcp.e2e.csproj --filter "Category=McpE2E.NoEnvironment"
```
