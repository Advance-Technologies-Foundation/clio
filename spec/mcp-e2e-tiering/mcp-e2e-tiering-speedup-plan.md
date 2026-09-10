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
3. Проба досяжності стенду (`ping-app`) — **один раз на фікстуру**, мемоізується **лише успіх**;
   `Assert.Ignore` лишається **всередині тесту** (не в `OneTimeSetUp`), щоб семантика skip не змінилась.
   Невдалу пробу свідомо не кешуємо: інакше одне вікно недоступності (recycle app-pool, перебудова OData)
   скіпало б усі 16 тестів фікстури зі застарілою причиною, не відрізнимо від ненастроєної машини
   (знахідка фінального review; виправлено в `ResolveEnvironmentOnceAsync`).
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
| `ApplicationSectionMaintenanceToolE2ETests` | 4 | 4 → 1 | 3–6 → 1–2 | lifecycle-тест ніколи не пробував |
| `WorkspaceSyncToolE2ETests` | 4 | 4 → 1 | — | shared-restore workspace уже мемоізований (ENG-92459) |
| `DbTemplatePruneToolE2ETests` (NoEnv-клас) | 3 | 3 → 1 | — | Sandbox-клас у тому ж файлі не чіпаємо |
| `WatchCompilationToolE2ETests` | 3 | 3 → 1 | 1 → 1 | feature-gate `SkipIfFeatureDisabled` лишається в тесті |
| `FindAppToolE2ETests` | 2 | 2 → 1 | — | |
| `InstallApplicationToolE2ETests` | 2 | 2 → 1 | 1 → 1 | |
| `RemovePackageDependencyToolE2ETests` | 2 | 2 → 1 | — | NoEnvironment |
| `ClearRedisToolE2ETests` | 2 | 2 → 1 | — | NoEnvironment |
| **Разом** | **63** | **63 → 14** (−49) | 28 → 3 викликів проби (−25 процесів при досяжному стенді, до −42 із fallback) | |

Очікуваний виграш: **49 × S + 25–42 × ~1 с** ≈ 4–9 хв wall-clock залежно від S на агенті. Виграш
залежить від лейну: фікстура, всі тести якої скіпаються (немає destructive opt-in / стенду), раніше не
стартувала жодного сервера, тепер стартує один — це ціна одного `[OneTimeSetUp]` на фікстуру.

**Не входить** (не верифікується статусом TeamCity, бо категорія `McpE2E.ProcessDesigner` виключена
фільтром job'а): `ModifyBusinessProcessToolE2ETests` (56 → 1), `CreateBusinessProcessToolE2ETests`
(26 → 1), `ValidateProcessGraph` (11), `DescribeProcess` (5), `ApprovalElement` (4),
`ModifyProcessAsNewVersion` (4), `SetActiveProcessVersion` (4). Той самий рефактор дає ~110 стартів,
але його може перевірити лише хтось зі стендом із `CrtProcessBuilder`. Окремий PR після цього.

**Ризики і як їх ловимо**
- *Стан, що живе між тестами у спільному процесі* — він є, і він продуктовий: сервер тримає один
  DI-контейнер і одну автентифіковану сесію Creatio на ключ середовища (`SessionContainerCache`, 5 хв
  idle-eviction), а після recycle стенду (`push-workspace`, `install-application`) наступний виклик
  іде зі старим cookie і покладається на `ReauthExecutor`. Раніше кожен тест логінився з нового процесу
  й цей шлях не перевірявся взагалі; тепер перевіряється — так само, як у реального агента з довгоживучим
  сервером. Жоден асерт конвертованих фікстур не залежить від «свіжого» логіну
  (запис: `docs/knowledge/Tests/shared-mcp-server-per-fixture-keeps-the-tenant-session-across-tests.md`).
  Якщо TeamCity покаже падіння одразу після recycle-тесту з login-сторінкою або `Could not verify package
  requirements` — це дефект re-auth-шляху продукту або harness-очікування recovery, не привід повертати
  старт-на-тест. `PlatformVersionResolver` (5 хв, per-env) конвертовані фікстури не читають.
- *Семантика `[OneTimeSetUp]`* — старт сервера більше не обмежений таймаутом кожного тесту (2–15 хв), а
  5-хвилинним CTS фікстури; провал старту тепер позначає **всі** тести фікстури як Failed, тоді як раніше
  тест із власним гейтом дав би Ignored. `McpFixturePolicyTests.SharedServerFixtures_ShouldKeepStandAccessAndSkipsOutOfOneTimeSetUp`
  пінить протилежний інваріант: жоден `[OneTimeSetUp]` не торкається стенду і не робить `Assert.Ignore`.
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

**Правки після фінального 3-lens review (gate 3).** Три лінзи (якість, коректність+продуктивність,
безпека) не знайшли Blocker/High у самій конверсії, але зійшлися на трьох речах, і всі виправлені тут:
(1) `SharedServerFixtures_ShouldKeepStandAccessAndSkipsOutOfOneTimeSetUp` **не перевіряв нічого** —
обидва `[OneTimeSetUp]` проєкту лежать поза його вибіркою (база в `Support/Mcp`, `SearchOption.TopDirectoryOnly`;
`McpSharedHomeSetUpFixture` не містить літерала `": McpContractFixtureBase"`), тож цикл порушень жодного разу
не виконався, а поріг `>= 13` стояв проти реальних 116 файлів. Тепер сканер обходить усі файли проєкту,
знаходить тіло методу підрахунком дужок (а не за `"\n\t}\n"`), **проходить один рівень виклику приватного
хелпера** (усталена форма в цьому сюїті — `Ensure*Async`), має anti-vacuity поріг за кількістю реально
прочитаних тіл і **позитивний контроль** — окремий тест, що доводить: пряме порушення й порушення через
хелпер справді репортяться. (2) `d2`-fallback у `SchemaSyncToolE2ETests` лишався незакритим, хоча PR
перетеговує цю фікстуру, а її env-шлях робить `push-workspace`, ставить `cliogate` і кешує вибір у
`_sharedEnvironmentName` на всю фікстуру — тепер той самий гейт `!AllowDestructiveMcpTests`, що в
`ApplicationTool`/`ApplicationSectionMaintenance`. (3) мемоізація проби кешувала й **невдачу**. Дрібніше:
guard паралельного пулу тепер ловить будь-яке написання `[Parallelizable]` (голе = `ParallelScope.Self`,
`.All`/`.Fixtures` — той самий пул) і сканує підкаталоги; у `DataBindingToolE2ETests` видалено мертвий
resolver стенду з `requireEnvironment = true` за замовчуванням; прибрано мертвий код у
`WorkspaceSyncToolE2ETests`; best-effort видалення тимчасових каталогів доведено до `SchemaSync` і
`DataBinding`; seed спільного `appsettings.json` у `McpServerSession` серіалізовано локом (пул зріс із 26 до
67 одночасних стартерів).

**Свідомо не в цьому PR:** незакритий `d2`-fallback ще у ~12 фікстурах, яких PR не чіпає (інваріант діє в
місцях, які ця конверсія зробила кешованими); `DataBindingDbFixtureBase` досі виконує `create-workspace` +
`add-package` для env-free тестів (16 зайвих процесів у швидкому лейні); база й конвертовані arrange
розходяться щодо `McpE2E:ClioProcessPath` (латентно — CI його не задає).

**Що зроблено (після зеленого 15999407).** Скринінг 73 NoEnvironment-only фікстур, що лишались поза
пулом, за чотирма ознаками: (а) `McpContractFixtureBase`/`DataBindingDbFixtureBase` (один сервер на
фікстуру); (б) жодного `Environment.SetEnvironmentVariable`; (в) `TemporaryClioSettingsOverride`/власний
старт сервера — лише з ізольованим домом; (г) інструменти, що не пишуть у спільний `CLIO_HOME`. Пройшли
**40 класів у 39 файлах** — від `AddPackage` до `UploadImage`, включно з ізольованими
`Knowledge*`, `GuidanceGetDiagnostics`, `RequestInfo`, `MobilePageConversionGuide` (обидва класи) і
трьома фікстурами, що взагалі не мали маркера (`ClearRedis`, `FindEmptyIisPort`, `RemovePackageDependency`).
Пул: 27 → 67 класів фікстур (26 → 65 файлів). `NumberOfTestWorkers` лишається 2.

Спочатку пройшов і 41-й — `ListEntityClientSchemasToolE2ETests`, — але паралельно з цим PR у `master`
злився PR #1399, який додав у ту саму фікстуру `McpE2E.Sandbox`-тест на типізовану сутність стенду.
GitHub Actions ганяє merge-коміт, тож `SandboxFixtures_ShouldBeNonParallelizable` впав саме там; фікстура
повернулась до `[NonParallelizable]`. Це і є очікувана робота інваріанту: пул визначає guard, а не історія файлу.

**Свідомо лишені серійними**: `ExperimentalToolE2ETests` (пише feature-флаги у спільний
`appsettings.json`), `ComponentInfoToolE2ETests` (пер-тестові сервери з власними env-змінними),
`BuildThemeToolE2ETests` (у файлі є фікстура з живою мережею без тиру), `SendTelemetry*`,
`ReadResponseDeadline`, `SettingsHealth`, `McpWorker*`, `CuratedKnowledge*`, `McpServerShutdown` та інші,
що стартують власні процеси або мутують оточення тест-хоста.

**Чому це безпечно для стенду.** NUnit виконує паралельні фікстури у власній «зміні» і не запускає їх
одночасно з `[NonParallelizable]`-чергою; крім того, жодна фікстура пулу не має `Sandbox`-тестів
(інваріант `McpFixturePolicyTests`). Єдині можливі колізії — між фікстурами самого пулу через спільний
`CLIO_HOME` або процесне оточення; саме їх відсіює скринінг, а новий guard у `McpFixturePolicyTests`
(`ParallelFixtures_ShouldNotMutateProcessEnvironmentOrSharedSettings`) не дає їм повернутись.

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
- TeamCity **не серіалізує** білди `Team_Atf_ClioMcpE2eTests` для різних комітів однієї гілки: скрипт черги
  (`.github/scripts/queue-teamcity-build.ps1`) дедуплікує лише той самий SHA, тож push у PR під час живого
  білду ставить другий **одночасний** full-Creatio білд. Спостереження 2026-09-09: 16000226 (tip `6ac7245`)
  стартував о `17:29Z` паралельно з 16000208 і впав за 2.5 хв, до першого тесту; 16000208 при цьому дійшов до
  success. Причина падіння звідси невидима (лог TeamCity доступний лише з корпоративної мережі). Практичний
  висновок для цього плану: не пушити наступний етап, поки статус попереднього білду ще `pending`, а статус
  коміту, що запустився поверх живого білду, вважати недійсним і перезапускати після його завершення.

---

## 7. Результати (заповнюється по ходу)

| Етап | Коміт | TeamCity build | Статус | Тривалість | Tests passed |
|---|---|---|---|---|---|
| baseline (PR #1399) | `77cc191` | 15999000 | success | **57m 32s** (статус pending `12:35:36Z` → success `13:33:08Z`; це весь білд — деплой Creatio + install-gate + seed + крок тестів, з яких ~44m 20s — тести за даними запиту) | статус не несе кількості (`"TeamCity build finished"`) |
| Stage 1 — локально | робоче дерево | — | 13 фікстур / 63 тести: 25 passed, 19 skipped (Ignore через недосяжний стенд / вимкнений feature-flag), 19 failed — усі 19 з одним і тим самим текстом `EnsureSandboxIsConfigured` (задокументований локальний fail-fast без `Sandbox:EnvironmentName`, відтворюється на `master`); `McpFixturePolicyTests` 11/11 | 49 с на 63 тести з 13 стартами замість 63 (`CreateRelatedPageAddon`: 4–96 мс/тест замість ~3.8 с) | — |
| Stage 1 + Stage 3 — TeamCity (PR #1427) | `c8472c0` | [15999407](https://teamcity-rnd.bpmonline.com/buildConfiguration/Team_Atf_ClioMcpE2eTests/15999407) | **success** | **≈50 хв** (черга `13:52:24Z` → success `14:42:56Z`; мітка pending недоступна через combined-status API, похибка ≈ ±30 с) — **−7.5 хв проти 57m 32s** (−13 % білду; ~−17 % тестового кроку, бо деплой незмінний). Це відповідає S ≈ 8 с на Windows-агенті при 49 знятих стартах + ~25 знятих `ping-app` | статус не несе кількості; порівняння `Tests passed` 15999000 vs 15999407 — у TeamCity |
| Stage 3 — локально | робоче дерево | — | `--filter Category=McpE2E.NoEnvironment` по 5 перетегованих фікстурах: **28 passed, 0 failed, 0 skipped** (до перетегування ці 28 у швидкому гейті не запускались узагалі) | 39 с | — |
| Stage 3 — TeamCity | `c8472c0` | 15999407 | success (той самий білд, що й Stage 1) | — | — |
| Stage 2 — локально | робоче дерево | — | повний тир `NoEnvironment`, 2 воркери: **485 тестів** (457 + 28 перетегованих), 480 passed, ті самі 2 контейнерні failures і 3 skips, що й у baseline — **жодного нового падіння чи skip** | **10m 58s** проти 12m 40s baseline (−13 %, при +28 тестах) | `McpFixturePolicyTests` 13/13 (новий guard `ParallelFixtures_ShouldNotMutateProcessEnvironmentOrSharedSettings`) |
| Stage 2 — TeamCity (PR #1427) | `b2d21c8` | [16000208](https://teamcity-rnd.bpmonline.com/buildConfiguration/Team_Atf_ClioMcpE2eTests/16000208) | **success** | **47m 04s** (pending `17:12:55Z` → success `17:59:59Z`) — **−3.3 хв проти Stage 1+3 (≈50m 20s) і −10.5 хв проти baseline 57m 32s** (−18 % білду; ≈ −24 % тестового кроку, якщо деплой ≈13 хв незмінний: 44m 20s → ≈33m 50s). Останні ~2.5 хв білд перекривався з 16000226 (рядок нижче) — на результат не вплинуло | статус не несе кількості; порівняння `Tests passed` 15999000 vs 16000208 — у TeamCity |
| merge `master` + CI-фікс — TeamCity | `6ac7245` | [16000226](https://teamcity-rnd.bpmonline.com/buildConfiguration/Team_Atf_ClioMcpE2eTests/16000226) | **failure за 2.5 хв** | queued `17:29:08Z` → failure `17:31:36Z`: упав до першого тесту (повний прогін ≈47–50 хв, сам деплой Creatio >10 хв), стартувавши **паралельно** з ще живим 16000208 — скрипт черги дедуплікує лише той самий коміт, тож новий коміт ставить другий одночасний full-Creatio білд (небезпека, описана в коментарі `queue-teamcity-build.ps1`). Лог TeamCity з контейнера недоступний (хост зовні резолвиться у публічний nginx з 404). На тому ж коміті GitHub Actions зелені, `clio.mcp.e2e` збирається під net8.0 і net10.0. Чистий перезапуск — наступний рядок | — |
| tip після завершення 16000208 — TeamCity | `23f7423` | [16000274](https://teamcity-rnd.bpmonline.com/buildConfiguration/Team_Atf_ClioMcpE2eTests/16000274) | **success** | **47m 17s** (pending `18:10:21Z` → success `18:57:38Z`), +13 с до 16000208 — той самий код паралельного пулу після мержу `master` (з `ListEntityClientSchemasToolE2ETests` знову серійною). Разом із 16000208 це **два поспіль зелені прогони** змін паралелізму, як вимагає ENG-92558; GitHub Actions на тому ж коміті зелені | статус не несе кількості |
