# Source of truth: автоматичний реєстр з трьома ізольованими доменами

Каталог компонентів — це data product. Його еволюція (поява компонентів, властивостей, deprecation) має відбуватися автоматично, без ручного редагування JSON у clio. Документ описує цільову архітектуру з трьома ізольованими доменами: **creatio-ui** (source), **composer** (integration), **clio** (consumer).

## Архітектурні рішення (закріплено)

1. **Source-of-truth — TS-декоратори + ViewConfig + JSDoc у creatio-ui.** Жодного ручного JSON-у на стороні платформи.
2. **Composer живе в окремому repo** `creatio-component-registry-composer`. Не в clio (змішує consumer і integration), не в creatio-ui (composer ≠ UI source).
3. **Distribution UI → composer**: NPM пакет `@creatio/component-registry` (semver = platform version, наприклад `8.3.0`).
4. **Distribution composer → clio**: NuGet пакет `Creatio.ComponentRegistry` (незалежний semver — див. політику нижче).
5. **clio споживає через `PackageReference`**, registry-дані ніколи не commit-яться у clio repo.
6. **Runtime serving — embedded resource у NuGet pkg**, читається через `Assembly.GetManifestResourceStream`. Жодного network call на runtime.
7. **Sharded authoring у NPM** (per-component JSON), **unified bundle у NuGet**. Composer collapses sharding під час merge.
8. **`supported-versions.json` і `overrides.json` — у composer-repo**, не в clio і не в creatio-ui.

## Цільова архітектура

```
                  ┌──────────── creatio-ui ────────────┐
                  │   @CrtViewElement + *ViewConfig    │
                  │   + JSDoc (@since, @aiCategory,    │
                  │           @aiHint, @deprecated)    │
                  │             │                      │
                  │   AST extractor (Jenkins on        │
                  │   branch-cut / push / GA-tag)      │
                  └─────────────┼──────────────────────┘
                                │ npm publish on GA-tag only
                                ▼
              ┌─────── @creatio/component-registry ─────┐
              │   per-version JSON snapshots            │
              │   (sharded внутрішньо: один файл per    │
              │    component, для review)               │
              └─────────────┼───────────────────────────┘
                            │ npm install (composer CI)
                            ▼
   ┌────── creatio-component-registry-composer ────────┐
   │   supported-versions.json (manual PR)             │
   │   overrides.json (AI team owned)                  │
   │   ──────────────────────                          │
   │   1. pull NPM snapshots для supported versions    │
   │   2. diff snapshots → availability ranges         │
   │   3. merge overrides                              │
   │   4. collapse sharding → single bundle            │
   │   5. stamp metadata.json (provenance)             │
   │   6. dotnet pack + nuget push                     │
   └─────────────┼─────────────────────────────────────┘
                 │ NuGet publish (independent semver)
                 ▼
       ┌─── Creatio.ComponentRegistry NuGet pkg ───┐
       │   ComponentRegistry.json (unified)        │
       │   metadata.json (provenance)              │
       │   $schema reference                       │
       └─────────────┼─────────────────────────────┘
                     │ <PackageReference> в Directory.Packages.props
                     ▼
           ┌─────── clio.csproj ────────┐
           │   ComponentInfoCatalog     │
           │     (reads embedded JSON   │
           │      через Assembly.       │
           │      GetManifestResource)  │
           │   IPlatformVersionResolver │
           │   MCP get-component-info   │
           └────────────────────────────┘
```

## Ownership matrix

| Repo | Власник | Source-of-truth для |
|---|---|---|
| **creatio-ui** | Platform-UI team | `@CrtViewElement` декоратори, `*ViewConfig` interfaces, JSDoc-метадані (`@since`, `@deprecated`, `@aiCategory`, `@aiHint`) |
| **creatio-component-registry-composer** | AI / clio team | `supported-versions.json`, `overrides.json`, composer-merge-logic, NuGet publish |
| **clio** | clio team | `ComponentInfoCatalog` loader, `IPlatformVersionResolver`, MCP tools, guidance resources |

Жоден з трьох не має write-access до іншого крім стандартного PR-flow. NPM і NuGet — **transport contracts**, не shared mutable state.

## Версіонування `Creatio.ComponentRegistry` (NuGet)

Незалежний semver, decoupled від обох (clio version, platform version). Правила бампу:

- **MAJOR** — breaking schema change. Наприклад, формат `availability` змінився, поле `componentType` перейменовано, top-level structure JSON-у не back-compat. Consumer (clio) потребує coordinated update.
- **MINOR** — нова supported platform line, нові компоненти, нові properties, нові категорії. Backwards-compatible — старий clio читає без модифікацій.
- **PATCH** — оновлення descriptions, defaults, examples, AI hints. Pure data refresh.

Це дає dependabot правильну поведінку: PATCH auto-merge, MINOR з review-checklist, MAJOR — блокуючий PR із migration plan на стороні clio.

## Schema versioning і metadata всередині пакету

NuGet pkg містить два файли:

**`ComponentRegistry.json`** (data) — top-level:

```json
{
  "$schema": "https://schema.creatio.com/component-registry/v1.json",
  "schemaVersion": "1.0",
  "latestKnownVersion": "8.3.2",
  "categories": [
    { "id": "containers", "order": 0, "label": "Containers" },
    ...
  ],
  "components": [ ... ]
}
```

`schemaVersion` — independent від `registryVersion` (NuGet pkg version). Consumer перевіряє лише schema-сумісність — додатковий запобіжник проти несумісних змін.

**`metadata.json`** (provenance):

```json
{
  "registryVersion": "1.5.0",
  "schemaVersion": "1.0",
  "buildTime": "2026-05-12T10:00:00Z",
  "supportedPlatformVersions": ["8.0.x", "8.1.x", "8.2.x", "8.3.x"],
  "latestKnownVersion": "8.3.2",
  "sources": {
    "creatioUi": {
      "8.0.x": { "npmVersion": "8.0.18", "gitSha": "abc..." },
      "8.1.x": { "npmVersion": "8.1.7",  "gitSha": "def..." },
      "8.2.x": { "npmVersion": "8.2.3",  "gitSha": "ghi..." },
      "8.3.x": { "npmVersion": "8.3.2",  "gitSha": "jkl..." }
    },
    "composer": { "gitSha": "mno...", "version": "2.4.1" },
    "overrides": { "gitSha": "mno...", "appliedCount": 17 }
  }
}
```

Це закриває диагностику. На питання «чому в clio `1.10` з `Creatio.ComponentRegistry 1.5.0` AI не бачить `crt.NewWidget`, який є в creatio-ui 8.3 branch?» — відкриваємо metadata, бачимо, що `1.5.0` побудовано до того, як `8.3.x` потрапив у `supported-versions.json`.

## Sources в creatio-ui

Витяг із [03-extraction-analysis.md](03-extraction-analysis.md) і дослідження репо:

| Джерело | Що дає | Якість |
|---|---|---|
| `@CrtViewElement({ type: 'crt.X' })` декоратор | Канонічне ім'я компонента | **Високо** — AST-видобувне |
| `*ViewConfig` TS-interface (наприклад `ButtonViewConfig`) | Контракт властивостей з типами | **Високо** — TS-typed, JSDoc-friendly |
| `@CrtInterfaceDesignerItem` декоратор | Defaults (`defaultPropertyValues`), `typeCaption`, `viewElementGroupType` | **Високо** — designer metadata |
| `api-extractor` (`docModel`) | JSON-rollup public API | **Середньо** — не sees decorator content |
| Runtime registry (`BaseViewElementRegistry`) | Ground truth після bootstrap | **Високо**, але важко в CI |

**Інклюзивний критерій:** клас із декоратором `@CrtViewElement`. Жодних інших евристик.

**Ексклюзивні фільтри:**

| Категорія | Glob |
|---|---|
| Тестові файли | `**/*.spec.ts`, `**/*.spec.ui.ts`, `**/*.spec.tsx`, `**/*.test.ts`, `**/*.test.tsx` |
| Моки | `**/*.mock.ts`, `**/mocks/**`, `**/__mocks__/**` |
| Built artifacts | `apps/pkgs/**` |
| Designer-only UI (lib) | `libs/studio-enterprise/ui/interface-designer-properties-panel/**` |
| Designer-only UI (subpath) | `**/designtime/**` |
| Стандартні | `**/node_modules/**`, `dist/**` |

`**/designtime/**` — точний інваріант: 44/44 `*PropertiesPanel` у `designtime/`, 0/192 не-PropertiesPanel у `designtime/`. Перевірено на `creatio-ui@master`.

**Critical:** парсер ігнорує декоратор `@CrtViewElement` всередині JSDoc-коментарів (інакше підхопить `usr.Example` з прикладів у файлах декоратор-визначень). Реалізація — ts-morph node-level decorator API, не текстовий пошук.

## Extractor pipeline (NPM package authoring)

1. **AST walk** усіх `*.ts` із застосуванням фільтрів.
2. Для кожного класу з `@CrtViewElement`:
   - `componentType` = `decorator.arguments[0].type` (string literal).
   - **БЕЗ** semantic-фільтрації по суфіксах. `*PropertiesPanel`, `*Request`-named включаються — їхнє приховування делегується composer-овському `overrides.json` (`aiHidden: true`).
3. Резолвити пов'язаний `ViewConfig` interface (конвенція: `<ComponentName>ViewConfig` у `view-models/`).
4. Для кожного property у ViewConfig:
   - `type` — текстова репрезентація TS-типу
   - `description` — JSDoc `@description` або leading-коментар
   - `required` — відсутність `?` модифікатора
   - `values` — для union-типів стрічкових літералів
   - `default` — з `@CrtInterfaceDesignerItem.defaultPropertyValues[name]`
   - `availability` — з JSDoc `@since`/`@deprecated`
5. `category` — JSDoc `@aiCategory` overrides; інакше — мапінг через `viewElementGroupType`.
6. **Validation step**: порівняти AST-видобутий set із `BaseViewElementRegistry` runtime (опційний job). Розбіжності → fail.
7. **Output:** sharded JSON у NPM-пакет — один файл per component (`components/crt.Button.json`, `crt.Input.json`, …) + `manifest.json` із загальними метаданими.

## JSDoc-вокабуляр у creatio-ui

```typescript
/**
 * @aiCategory interactive
 * @aiHint "Use crt.ButtonToggleGroup for segmented selection"
 */
@CrtViewElement({ type: 'crt.Button', ... })
export class CrtButtonComponent { ... }

export interface ButtonViewConfig extends ... {
    /** Button caption. */
    caption?: string;

    /**
     * Icon placement.
     * @since 8.1.0
     */
    iconPosition?: IconPositionEnum;

    /**
     * @deprecated 8.2.0 - replaced by crt.ButtonToggleGroup
     */
    legacyStyleMode?: string;
}
```

Вокабуляр (рекомендований для lint-правила):

- `@since <version>` → `availability.since`
- `@deprecated <version> - <reason>` → `availability.until` + опис suffix-ується reason-ом
- `@aiCategory <name>` → категорія (containers/fields/interactive/display/filtering)
- `@aiHint "..."` → інлайн-порада для AI

`@aiHidden` / `@aiInclude` свідомо **видалено** з вокабуляру: інклюзивний критерій лише `@CrtViewElement`, шумні компоненти приховуються composer-овським `overrides.json`. Розділення відповідальності: платформа звітує про все; AI-team вирішує, що показувати.

## Composer-логіка

Repo: `creatio-component-registry-composer`. Файли під версією:

- `supported-versions.json` — список minor-ліній, які composer враховує:
  ```json
  ["8.0.x", "8.1.x", "8.2.x", "8.3.x"]
  ```
- `overrides.json` — AI-специфічні правила:
  ```json
  {
    "aiHidden": ["crt.TableBooleanCell", "crt.RouterOutlet", "crt.ModuleLoader"],
    "aiOverlay": {
      "crt.EmailInput": {
        "aiHint": "For display-only email rendering prefer crt.ToEmailLink converter."
      }
    }
  }
  ```
- `composer.config.ts` — runtime config (NuGet feed URL, npm registry URL, kerb auth).

**CI-пробіг (Jenkins у composer-repo):**

1. Тригер: cron (щодоби), webhook на npm `@creatio/component-registry` publish, або manual.
2. Pull `@creatio/component-registry@<v>` для всіх версій із `supported-versions.json` (latest patch у кожній minor-лінії).
3. Збираємо unified bundle:
   - для кожного `componentType`: якщо є у `vN`, нема в `vN-1` → `availability.since = vN`; якщо був у `vN-1`, зник у `vN` → `availability.until = vN`. Те саме на рівні properties.
4. Apply `overrides.json`:
   - `aiHidden` записи виключаються повністю.
   - `aiOverlay` мерджиться поверх auto-data (overlay wins for описних полів).
   - Conflict-detection: якщо `aiOverlay` посилається на `componentType`, який не існує в жодній з підтримуваних версій — composer падає з ошибкою (захист від stale overrides).
5. Stamp `metadata.json` із provenance (NPM versions, git SHA-и).
6. `dotnet pack` + `dotnet nuget push` у internal NuGet feed.
7. Bump version per semver-rule:
   - детектуємо breaking schema-changes → MAJOR;
   - детектуємо нові компоненти / properties / supported version → MINOR;
   - інше → PATCH.
8. Push git tag `v<X.Y.Z>` у composer-repo для traceability.

## Failure-mode design

Архітектурний інваріант: **кожен шар деградує незалежно; consumer на runtime не залежить від мережі.**

| Сценарій | Поведінка |
|---|---|
| NPM creatio-ui недоступний | Composer не build-ить нову версію → clio тримає попередню NuGet pinned version → AI бачить останній стабільний registry |
| Composer CI зламаний | NuGet feed має попередні версії → clio працює; dependabot тимчасово не бамп-ить |
| Internal NuGet feed недоступний | Build clio падає; локальний NuGet cache рятує більшість dev-кейсів; existing clio інсталяції unaffected |
| Composer випустив bad NuGet | Pin попередню version у `Directory.Packages.props` (1 рядок) → новий clio build → instant rollback |
| AI клієнт offline | `Creatio.ComponentRegistry` embedded у clio binary через NuGet content → 0 runtime network dependency |
| Schema breaking change | MAJOR bump → clio CI fail на старому loader → coordinated migration PR |

Жоден сценарій не торкається AI runtime UX — це архітектурна сила NuGet-варіанту проти cloud-fetch.

## Інтеграція в clio

### `Directory.Packages.props`

```xml
<Project>
  <ItemGroup>
    <PackageVersion Include="Creatio.ComponentRegistry" Version="1.5.0" />
    ...
  </ItemGroup>
</Project>
```

### `clio.csproj`

```xml
<ItemGroup>
  <PackageReference Include="Creatio.ComponentRegistry" />
</ItemGroup>
```

### `ComponentInfoCatalog` loader

Заміна поточної реалізації:

```csharp
// Поточне:
// string registryPath = Path.Combine(executingDirectory, "Command", "McpServer", "Data", "ComponentRegistry.json");

// Цільове:
using var stream = typeof(ComponentRegistryEntry).Assembly
    .GetManifestResourceStream("Creatio.ComponentRegistry.ComponentRegistry.json")
    ?? throw new InvalidOperationException("Embedded registry resource not found.");
```

Файл [clio/Command/McpServer/Data/ComponentRegistry.json](../clio/Command/McpServer/Data/ComponentRegistry.json) **видаляється** з clio repo. Loader читає з NuGet-вбудованого embedded resource.

## Release branch lifecycle у creatio-ui

Базовий тригер `push у release-branch` працює для **вже існуючих** ліній. Окремо — подія «creatio-ui виділяє нову гілку для майбутнього релізу» (`master` форкається у `8.3.x`).

**Політика:** prerelease-публікацій немає. Внутрішні extractor-build-и на release-branch до GA-тега в npm не публікуються — тільки локальні Jenkins-артефакти.

```
master (8.4 dev) ─┬─ cut → 8.3.x branch created
                  │     │
                  │     ├─ Jenkins on-branch-create (Jenkinsfile.Registry)
                  │     │    ├─ extract @ branch HEAD
                  │     │    ├─ baseline component-registry.json як Jenkins artifact
                  │     │    │   (інспекція + регресійний детектор; НЕ npm-publish)
                  │     │    └─ diff vs previous-line GA snapshot → preview звіт
                  │     │
                  │     └─ ongoing commits → re-extract, refresh artifact
                  │          (теж без npm-publish)
                  │
                  └─ tag 8.3.0 (GA) ─ extract → npm publish @creatio/component-registry@8.3.0
                                            └─ composer-CI cron picks up next run
                                                  └─ manual PR у composer-repo:
                                                      supported-versions.json += "8.3.x"
                                                        └─ composer rebuilds → NuGet bump
                                                            └─ clio dependabot bump
```

Етапи:

1. **Branch-cut у creatio-ui.** Jenkins запускає `Jenkinsfile.Registry` у baseline-mode: extract → Jenkins artifact + preview-діфф vs остання GA-публікація попередньої лінії. **No npm publish.** Це ловить регресії extractor-а і неузгодженості ДО GA.

2. **Ongoing commits на release-branch до GA.** Той самий pipeline re-runs, refresh-ить artifact. Composer не торкається.

3. **GA tag.** `npm publish @creatio/component-registry@8.3.0`. npm dist-tag `latest` оновлюється тільки якщо це max-версія.

4. **Реєстрація у composer-repo.** Manual PR у `supported-versions.json` (свідоме рішення команди підтримувати нову лінію). Auto-PR від npm-monitoring бота — опційне прискорення.

5. **Composer-пробіг.** Перебудовує unified registry, bump-ить NuGet semver.

6. **clio bump.** Dependabot/Renovate створює PR із bumped `<PackageVersion Include="Creatio.ComponentRegistry" Version="x.y.z" />`. Review checklist відповідно до semver-bump-рівня.

7. **Branch retirement.** PR у composer-repo, що знімає лінію з `supported-versions.json`. Composer:
   - якщо компонент/property відсутній у решті ліній — `availability.until` отримує значення першої не-підтримуваної версії;
   - якщо присутній у всіх — без змін.

## Закриті раніше відкриті питання

| # | Питання | Рішення |
|---|---|---|
| 1 | Власник `tools/component-registry-extractor/` | Platform-UI team (у creatio-ui repo) |
| 2 | JSDoc-вокабуляр як стандарт | Так, з eslint-правилом обов'язковості при додаванні нового `@CrtViewElement` |
| 3 | Хто володіє `overrides.json` | AI / clio team у composer-repo |
| 4 | Версійна політика подачі AI | `latest patch` кожної supported minor-лінії; AI бачить unified registry |
| 5 | Внутрішній npm registry | `docker-rnd.creatio.com` екосистема (existing) |
| 6 | Backfill v8.0/v8.1 | Forward-only від першої лінії, що буде у `supported-versions.json` на момент launch |
| 7 | `composable-apps/*` | Окремий extractor pass із tag-ом `scope: "app"` в record (поза скоупом v1) |
| 8 | Properties panels | Виключаються `**/designtime/**` фільтром на extractor-рівні |
| 9 | Branch-creation hook у Jenkins | Якщо немає — nightly diff-job (`git branch -r` vs cached) |
| 10 | npm dist-tag policy | `latest` = max версія + per-line tag (`8.2`, `8.3`) для backwards-compat consumer-pinning |
| 11 | Cadence GA-тегів | Per build (`8.3.0`, `8.3.1`, …); composer-cron підбирає в наступному циклі |
| 12 | Реєстрація підтримки | Manual PR у `supported-versions.json` (default); auto-PR бот — опційний |

## Залишаються до закриття (нижчого рівня)

- **MAJOR-bump migration path.** Якщо schema v2 з'являється, як clio підтримує обидві версії одночасно? Multi-target reader; адаптер у `ComponentInfoCatalog` — окремий design issue.
- **Overrides versioning.** `overrides.json` теж має semver чи живе як частина composer git history? Перше чистіше, друге простіше.
- **Telemetry contract.** Чи composer записує use-frequency у NuGet metadata, щоб back-port популярних overrides у `creatio-ui` як JSDoc? Сильна оптимізація, але вимагає телеметрії від MCP server-ів — окремий design issue.
- **Композиція `composable-apps/*` компонентів.** Як їх scope-увати у unified registry — окремий design issue для v2.

## Чому NuGet, а не in-repo JSON

Розглядалися альтернативи фінального зберігання — single embedded JSON commit-нутий у clio repo, per-component sharded у clio, SQLite, cloud-fetch. Деталі порівняння — у git history (PR #595, видалений файл `06-storage-distribution-analysis.md`).

NuGet-вибір розв'язує 4 конкретні проблеми, які тягне in-repo JSON commit:

1. **Auto-PR noise.** Composer-бот робить commit registry-діффу 500KB–1MB на кожен пробіг. PR-review цього diff-у безкорисний (величезний автоген).
2. **Implicit versioning.** clio v1.10 і v1.11 з різними registry-станами ідентифікуються тільки git-blame-ом. NuGet semver дає explicit pin.
3. **Dependabot-blind.** Bot-commit не моніториться dependabot/Renovate. NuGet PackageReference — стандарт.
4. **Parallel-PR collisions.** Code-PR і registry-bump-PR постійно стикаються в одному файлі. NuGet pin живе в `Directory.Packages.props`, code — окремо.

Цина: один додатковий step у composer pipeline (`dotnet pack` + `dotnet nuget push`) і одна додаткова налаштована залежність у clio. Втрата візуальної інспекції JSON у IDE mitigated публікацією human-readable artifact-у в Jenkins / release notes.

## Що НЕ робити (антипатерни)

- **Не commit-ити composed JSON у clio repo.** Втрачаємо explicit versioning, dependabot, parallel-PR ergonomics. Final storage = NuGet pkg, не git-tracked файл.
- **Не парсити `*.api.md` як JSON.** Markdown rollup призначений для review, не для парсингу.
- **Не зберігати per-version snapshot файли у clio repo.** Підтримка 10 версій = 10× дублювання. Замість цього — NPM-залежності у composer і unified bundle у NuGet.
- **Не робити decorator-extraction через regex.** AST-walk через `ts-morph`.
- **Не блокувати реліз creatio-ui на extractor.** Build registry окремим job, який падає isolated.
- **Не комітити registry-дані з write-access composer-а у clio.** Composer пише тільки в NuGet feed. clio підбирає через PackageReference.

## Артефакти інфраструктури, які можна реюзати

- **api-extractor конфіги** у `libs/devkit/{common,base,interface-designer}` — `apiReport.enabled: true`. Можна використати як вторинне джерело валідації.
- **Nx monorepo** у creatio-ui — `nx.json`, `project.json` у кожній lib — стандартний шлях для extractor-таски.
- **Jenkins pipeline-library** — `@Library('pipeline-library')` патерн вже використовується.
- **`docker-rnd.creatio.com`** — внутрішня екосистема публікації артефактів (NPM + NuGet feeds).
- **dependabot/Renovate** — стандартний інструмент для bump-у NuGet залежностей у clio.
