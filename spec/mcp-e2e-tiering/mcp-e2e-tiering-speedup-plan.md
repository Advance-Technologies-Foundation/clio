# План пришвидшення `clio.mcp.e2e` — реалізація з верифікацією на стенді

> Продовження [mcp-e2e-tiering-level-audit.md](mcp-e2e-tiering-level-audit.md) (аналіз) і
> ENG-92558 ([spec/archive/mcp-e2e-noenvironment-parallelization](../archive/mcp-e2e-noenvironment-parallelization/adr/adr-mcp-e2e-noenvironment-parallelization.md)),
> яке зробило спільний сервер + паралелізм для **чистих NoEnvironment** фікстур і свідомо лишило
> «serial Sandbox floor» на потім. Цей план — саме про той floor.
>
> **Гілка:** `claude/repo-update-z18z41` · **Дата:** 2026-09-09

---

## 1. Що вимірюємо і як

| Метрика | Джерело | Baseline |
|---|---|---|
| Wall-clock кроку TeamCity `Team_Atf_ClioMcpE2eTests` | commit status `CLIO MCP e2e tests (ATF)` на head SHA PR: `created_at` статусу *pending* → `created_at` фінального статусу | PR #1399, build 15999000: старт `12:35:36Z`, фініш — див. §7 |
| Старт одного `clio mcp-server` (S) | локально: тривалість тесту у фікстурі зі стартом-на-тест (`GetRelatedPageAddonToolE2ETests`) | **3.6–3.9 с** на Linux/Debug (виміряно, trx); на Windows-агенті очікувано 2–3× більше |
| Кількість стартів сервера у наборі TeamCity | статичний підрахунок | **226** на 772 тести |
| Прихованих стартів `clio ping-app` на тест | статичний підрахунок | 35 фікстур пробують стенд на кожен тест |
| `Skipped` у тирі `NoEnvironment` без стенду | локальний прогін `--filter Category=McpE2E.NoEnvironment` | має бути 0 (критерій ENG-92150) |
| Локальний тир `NoEnvironment` (2 воркери, Linux, Debug, `--no-build`) | trx `baseline-noenv` | **457 тестів, 12m 31s**; 452 passed, 2 failed (`McpWorkerContainment…OnUnix`, `CuratedKnowledgeGitStartup…` — специфіка контейнера, не змінюються цим планом), 3 skipped (Windows-only + 2 deploy-port); найдовші: `FlatArgsProgressToken` 102 с, `McpWorkerWedge` 34 с, `ReadResponseDeadline` 30 с, **`CreateRelatedPageAddon` 27 с / 7 тестів** |

Верифікація кожного етапу — **push у PR → статус `CLIO MCP e2e tests (ATF)`**: зелений + тривалість менша за
baseline. Локально перед кожним push: `McpFixturePolicyTests` (інваріанти) + змінені фікстури з тиру
`NoEnvironment` + повний `NoEnvironment` sweep.

---

## 2. Модель витрат (звідки беруться хвилини)

```
крок TeamCity ≈ Σ(старти сервера × S)  +  Σ(ping-app проби × ~1 с)  +  робота проти стенду  +  round-trip'и
                 226 × S                    ~120 × 1 с
```

При S ≈ 10 с (Windows, оцінка з коментаря `McpContractFixtureBase`) старти — ~38 хв послідовно, ~19 хв
при `NumberOfTestWorkers=2`, і майже всі вони у **`[NonParallelizable]`** фікстурах, тобто на критичному
шляху. Це і є «Sandbox floor» з ENG-92558.

---

## 3. Етапи

### Stage 1 — спільний сервер на фікстуру там, де конфіг між тестами не відрізняється

**Ідея.** Розширити патерн `McpContractFixtureBase` (ENG-92558 застосував його до чистих NoEnvironment
фікстур) на Sandbox-фікстури, чиї тести стартують сервер з **ідентичними** `TestConfiguration.Load()`
налаштуваннями. Сервер `clio mcp-server` не тримає стану стенду між викликами (стан живе у Creatio;
per-key семафори `ApplicationSectionCreateSerializationGuard` звільняються після кожного виклику;
`_reachableToolNames`/`_toolContractIndex` кешуються на стороні клієнта і не змінюються за життя процесу).

**Що змінюється в кожній фікстурі**
1. `: McpContractFixtureBase` — сервер стартує один раз у `[OneTimeSetUp]`.
2. Локальний `ArrangeContext.DisposeAsync` перестає диспозити `Session` (диспозить тільки CTS і тимчасові каталоги).
3. Проба досяжності стенду (`ping-app`) — **один раз на фікстуру**, мемоізується; `Assert.Ignore`
   лишається **всередині тесту** (не в `OneTimeSetUp`), щоб семантика skip не змінилась.
4. Жоден асерт, аргумент, категорія, `[NonParallelizable]` не змінюються.

**Виключення (лишаються зі своїм сервером)** — тести, чий сервер має стартувати з особливим оточенням:
`DataForgeStatus_Should_Ignore_Poisoned_Proxy_Environment_Variables` (отруює `HTTP(S)_PROXY` перед
стартом дитини), усі тести `ApplicationSectionToolE2ETests` з власним `appsettings.json`,
`ReadResponseDeadline*`, `FlatArgsProgressToken*`, `McpWorker*`, `McpHttp*`.

| Фікстура | Тестів | Стартів до → після | ping-app до → після | Особливості |
|---|---:|---|---|---|
| `ApplicationToolE2ETests` | 16 | 16 → 1 | 16–32 → 1–2 | `Should_Stream_PerPhase_Progress_Markers` за потреби стартує **приватну** сесію для re-login — лишається |
| `DataForgeToolE2ETests` | 9 | 9 → 2 | 9 → 1 | proxy-тест зі своїм сервером |
| `CreateRelatedPageAddonToolE2ETests` | 7 | 7 → 1 | — | NoEnvironment |
| `LinkFromRepositoryToolE2ETests` | 5 | 5 → 1 | — | temp-каталоги лишаються per-test |
| `GetRelatedPageAddonToolE2ETests` | 4 | 4 → 1 | — | NoEnvironment |
| `ApplicationSectionMaintenanceToolE2ETests` | 4 | 4 → 1 | 4–8 → 1–2 | |
| `WorkspaceSyncToolE2ETests` | 4 | 4 → 1 | — | shared-restore workspace уже мемоізований (ENG-92459) |
| `DbTemplatePruneToolE2ETests` (NoEnv-клас) | 3 | 3 → 1 | — | Sandbox-клас у тому ж файлі не чіпаємо |
| `WatchCompilationToolE2ETests` | 3 | 3 → 1 | 1 → 1 | feature-gate `SkipIfFeatureDisabled` лишається в тесті |
| `FindAppToolE2ETests` | 2 | 2 → 1 | — | |
| `InstallApplicationToolE2ETests` | 2 | 2 → 1 | 1 → 1 | |
| `RemovePackageDependencyToolE2ETests` | 2 | 2 → 1 | — | NoEnvironment |
| `ClearRedisToolE2ETests` | 2 | 2 → 1 | — | NoEnvironment |
| **Разом** | **63** | **63 → 14** (−49) | ~−40 проб | |

Очікуваний виграш: **49 × S + ~40 × 1 с** ≈ 4–9 хв wall-clock залежно від S на агенті.

**Не входить** (не верифікується статусом TeamCity, бо категорія `McpE2E.ProcessDesigner` виключена
фільтром job'а): `ModifyBusinessProcessToolE2ETests` (56 → 1), `CreateBusinessProcessToolE2ETests`
(26 → 1), `ValidateProcessGraph` (11), `DescribeProcess` (5), `ApprovalElement` (4),
`ModifyProcessAsNewVersion` (4), `SetActiveProcessVersion` (4). Той самий рефактор дає ~110 стартів,
але його може перевірити лише хтось зі стендом із `CrtProcessBuilder`. Окремий PR після цього.

**Ризики і як їх ловимо**
- *Стан, що протікає між тестами через спільний процес* — єдиний відомий: кеш `PlatformVersionResolver`
  (5 хв, per-env), нешкідливий. Якщо TeamCity покаже падіння, яке зникає при ізоляції — фікстура
  повертається на старт-на-тест (по одній, як каже ENG-92558 story 4).
- *`Assert.Ignore` у `OneTimeSetUp`* — не використовуємо: ігнор лишається per-test.
- *Порядок тестів* — не має значення: імена ресурсів унікальні (`Guid`).

### Stage 2 — розширити паралельний пул ізольованих NoEnvironment фікстур

ENG-92558 ввів 26 `[Parallelizable(ParallelScope.Self)]` фікстур і `NumberOfTestWorkers=2`; 136 фікстур
лишились `[NonParallelizable]`, серед них ~80 NoEnvironment. Кандидат — фікстура, яка **одночасно**:
(а) не містить жодного `McpE2E.Sandbox` тесту (інваріант `McpFixturePolicyTests`);
(б) не пише у спільний `appsettings.json` (`TemporaryClioSettingsOverride` без власного `HOME`/`CLIO_HOME`);
(в) не мутує `Environment.SetEnvironmentVariable` процесу тест-хоста;
(г) не стартує сервер поза `McpContractFixtureBase` з особливим оточенням (кожен такий старт — окремий процес, він безпечний, але його ціна не зменшується).

Робиться **після** Stage 1 і після того, як TeamCity підтвердить Stage 1: паралелізм множить будь-яку
приховану зв'язаність, тому спершу треба чистий сигнал. Обмеження з ENG-92558: воркерів ≤ 3, жодного
`[assembly: Parallelizable]`.

### Stage 3 — перетегувати 28 хибно-Sandbox тестів

`SchemaSyncToolE2ETests` (15), `DataBindingDbToolE2ETests` (8), `DataBindingToolE2ETests` (3 — уся
фікстура, тож клас-левел тег стає `NoEnvironment`), `ClearBrowserSessionToolE2ETests` (1),
`GetBrowserSessionToolE2ETests` (1) — клас-левел `[Category("McpE2E.Sandbox")]` → пер-методні
категорії за правилом тирингу (`requireEnvironment: false` / `requireReachableEnvironment: false` в
arrange ⇒ `NoEnvironment`, інакше `Sandbox`). Аудит нарахував 29: двадцять дев'ятий —
`DataForgeStatus_Should_Ignore_Poisoned_Proxy_Environment_Variables` — резолвить досяжний стенд перед
отруєнням proxy-змінних, тобто справедливо `Sandbox`; лишається. Змішані фікстури лишаються
`[NonParallelizable]` (містять Sandbox-тести; інваріант `McpFixturePolicyTests`). Wall-clock TeamCity не
змінює; повертає 28 тестів у швидкий гейт і робить критерій `Skipped == 0` досяжним. Верифікація:
локальний прогін цих фікстур під `--filter Category=McpE2E.NoEnvironment` — усі 28 виконуються, 0 skipped.

### Не робимо в цьому PR
- **Пониження рівня** (видалення e2e-дублів, згортання 93 advertisement-тестів) — змінює покриття; це
  рішення команди за таблицею аудиту, окремий PR.
- Зміну `NumberOfTestWorkers` — лише з вимірами (ENG-92558 story 4 DoD).

---

## 4. Порядок і критерії приймання

| Крок | Дія | Приймання |
|---|---|---|
| 0 | Baseline: локальний `NoEnvironment` sweep (trx) + тривалість PR #1399 | числа в §7 |
| 1 | Stage 1 → коміт → `McpFixturePolicyTests` зелені → змінені NoEnv-фікстури зелені локально → push | `CLIO MCP e2e tests (ATF)` = success; `Tests passed` не менше baseline; тривалість менша |
| 2 | Stage 3 → коміт → локальний `NoEnvironment` sweep: `Skipped == 0` серед перетегованих → push | статус success; локально +28 тестів у тирі |
| 3 | Stage 2 → коміт → push | статус success **двічі поспіль** (вимога ENG-92558 для паралелізму) |
| 4 | Фінальний 3-lens agentic review (AGENTS.md gate 3) | Blocker/High = 0 |

Кожний етап — окремий коміт; відкат = revert одного коміту.

---

## 5. Локальне відтворення

```bash
# baseline / після змін — тир без стенду (потрібне будь-яке зареєстроване активне середовище,
# інакше кожен invalid-env тест отримує "settings bootstrap is broken" замість "not found")
dotnet test clio.mcp.e2e/clio.mcp.e2e.csproj -f net10.0 --no-build \
  --settings clio.mcp.e2e/clio.mcp.e2e.runsettings \
  --filter "Category=McpE2E.NoEnvironment&Category!=McpE2E.Manual" --logger trx

# інваріанти
dotnet test clio.tests/clio.tests.csproj -f net10.0 --no-build --filter "FullyQualifiedName~McpFixturePolicyTests"
```

---

## 6. Що з'ясувалось під час підготовки (не з коду)

- Локально без зареєстрованого активного середовища **всі** `*_Should_Report_Invalid_Environment` тести
  червоні з текстом *«clio settings bootstrap is broken»*: `SettingsBootstrapReport.CanExecuteEnvTools`
  = `resolvedEnvironment is not null`, тобто вимагає `ActiveEnvironmentKey` → існуюче середовище. На
  TeamCity воно завжди є (sandbox). Для локального гейту досить `clio reg-web-app <name> -u http://127.0.0.1:9 …`.
- `TemporaryClioSettingsOverride` резолвить шлях до `appsettings.json` через **запуск `clio info --settings-file`** —
  ще один прихований процес на кожне використання.
- Проба досяжності робиться через `clio ping-app` (окремий процес), у `ApplicationToolE2ETests` та
  `ApplicationSectionMaintenanceToolE2ETests` — до **двох** проб на тест (configured + fallback `d2`).

---

## 7. Результати (заповнюється по ходу)

| Етап | Коміт | TeamCity build | Статус | Тривалість | Tests passed |
|---|---|---|---|---|---|
| baseline (PR #1399) | `77cc191` | 15999000 | pending → … | … | … |
| Stage 1 — локально | робоче дерево | — | 13 фікстур / 63 тести: 25 passed, 19 skipped (Ignore через недосяжний стенд / вимкнений feature-flag), 19 failed — усі 19 з одним і тим самим текстом `EnsureSandboxIsConfigured` (задокументований локальний fail-fast без `Sandbox:EnvironmentName`, відтворюється на `master`); `McpFixturePolicyTests` 11/11 | 49 с на 63 тести з 13 стартами замість 63 (`CreateRelatedPageAddon`: 4–96 мс/тест замість ~3.8 с) | — |
| Stage 1 — TeamCity | … | … | … | … | … |
| Stage 3 — локально | робоче дерево | — | `--filter Category=McpE2E.NoEnvironment` по 5 перетегованих фікстурах: **28 passed, 0 failed, 0 skipped** (до перетегування ці 28 у швидкому гейті не запускались узагалі) | 39 с | — |
| Stage 3 — TeamCity | … | … | … | … | … |
| Stage 2 | … | … | … | … | … |
