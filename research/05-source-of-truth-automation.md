# Source of truth: автоматичне оновлення каталогу з creatio-ui

При еволюції компонентів каталог [ComponentRegistry.json](../clio/Command/McpServer/Data/ComponentRegistry.json) має оновлюватися автоматично, на основі коду платформи. Це дослідження джерел SoT у репо `creatio-ui` і pipeline-у автоматизації.

> Примітка: у постановці згадано TeamCity. Фактично у `creatio-ui` працює **Jenkins** (`.pipeline/Jenkinsfile*`). Тож «план на TeamCity» з постановки треба читати як «CI/CD-pipeline» — конкретна реалізація буде Jenkins у creatio-ui, або TeamCity у clio-стороні (якщо це звичний оркестратор для clio). Оркестратор взаємозамінний.

## Артефакти-кандидати на SoT у `creatio-ui`

| Джерело | Що дає | Якість |
|---|---|---|
| `@CrtViewElement({ type: 'crt.X' })` декоратор | Канонічне ім'я компонента, мапінг на TS-клас | **Високо** — AST-видобувне, ~327 декорувань в `libs/**/*.ts` |
| `*ViewConfig` TS-interface (наприклад `ButtonViewConfig`) | Повний контракт властивостей з типами, optional/required | **Високо** — TS-typed, JSDoc-friendly |
| `@CrtInterfaceDesignerItem` декоратор | Toolbar-позиція, defaults (`defaultPropertyValues`), `typeCaption`, `viewElementGroupType`, `propertiesPanel` reference | **Високо** — designer metadata, defaults |
| `api-extractor` (`docModel`) | Стабільний JSON-rollup публічного API | **Середньо** — вже сконфігуровано для `devkit/common,base,interface-designer`, `docModel: false`. Бачить тільки експорти, не sees decorator content |
| Runtime registry (`BaseViewElementRegistry`) | Ground truth: що реально зареєстровано після bootstrap | **Високо** — потребує запуску Angular app, найважче в CI |

### Приклад декорацій

`libs/studio-enterprise/ui/components/src/lib/button/components/button.component.ts`:

```typescript
@CrtViewElement({
    type: 'crt.Button',
    reuseStrategy: ViewElementReuseStrategy.Reuse,
})
@CrtInterfaceDesignerItem({
    toolbarConfig: {
        position: 10,
        icon: require('!!raw-loader?{esModule:false}!../../../assets/button.svg'),
        defaultPropertyValues: {
            caption: '',
            color: 'default',
            disabled: false,
        },
        defaultLocalizableStrings: {
            caption: 'Components.Button.Caption',
        },
    },
    propertiesPanel: 'crt.ButtonPropertiesPanel',
    collectionPropertyNames: ['menuItems'],
    typeCaption: 'Components.Button.Caption',
    viewElementGroupType: ViewElementGroupType.Components,
})
@Component({
    selector: 'crt-button',
    ...
})
export class CrtButtonComponent extends CrtBaseButtonComponent {}
```

`libs/studio-enterprise/ui/components/src/lib/button/view-models/button-view-config.model.ts`:

```typescript
export interface ButtonViewConfig extends ViewElementConfig<BaseElementConfig> {
    disabled?: boolean | string;
    caption?: string;
    icon?: string | ButtonIcon | ButtonAnimatedIcon;
    size?: SizeEnum;
    iconSize?: SizeEnum;
    iconPosition?: IconPositionEnum;
    ariaLabel?: string;
    title?: string;
    textTransform?: TextTransform;
    type?: ButtonType | string;
    displayType?: ButtonDisplayType;
    disableRipple?: boolean;
    isIconModeSizePx?: number;
    clickMode?: ButtonClickMode | string;
    menuItems?: CrtMenuItemViewElementConfig[] | string;
    menuPanelClass?: string;
    useGlassmorphism?: boolean;
    clicked?: RequestBindingConfig;
    color?: ButtonColor;
}
```

### Розмір розриву між source і curated registry

Станом на `creatio-ui@master`, після уточнених ексклюзивних фільтрів (test/mock + `apps/pkgs` + `interface-designer-properties-panel` + `**/designtime/**`):

- **192** унікальних `@CrtViewElement` декорувань (designer config UI вилучено на рівні фільтра)
- **92** записи у курованому `ComponentRegistry.json`
- **0** регресій (всі 92 курованих знаходяться в extracted-сеті)
- **0** `*PropertiesPanel` у extracted-сеті (повна санація `**/designtime/**`-фільтром)

З 192 extracted:
- **92** уже в курованому реєстрі
- **100** нових кандидатів, з них:
  - ~30 internal `Table*Cell` / `DataTableEdit*Cell` (рендеряться `DataGrid`, AI не інстанціює)
  - ~10 app-shell / infra (`AppBackground`, `RouterOutlet`, `ModuleLoader`, …)
  - ~5 deprecated (`DeprecatedInput`, `Angular7XDetail`, …)
  - **~55 справжніх UI-компонентів** для перевірки AI-командою (`ChatComposer`, `EmailComposer`, `EmojiSelect`, `Switch`, `TabPanelHeader`, `Timer`, `TranslateToggle`, `WaterfallWidget` тощо)

Деталі і повні списки — у [03-available-components.md](03-available-components.md#авто-екстракція-з-creatio-ui-актуалізовано).

**Висновок:** designer-only UI відсікаємо на рівні extractor-а (folder-фільтр — точний інваріант). Решта «шуму» (internal cells, app-shell, deprecated) — задача overlay-у в clio composer. Це знімає потребу в семантичних `@aiHidden`/`@aiInclude` тегах у platform code.

## Версійні якорі вже в репо

- Branches: `origin/8.1.3`, `origin/8.1.4`, `origin/8.1.5`, `origin/8.2.0`, `origin/8.2.2`, `origin/8.2.3`, `origin/8.3.x` — формат `<major>.<minor>.<build>`.
- `git log` має semver-теги для деяких піддерев.
- Кожен `package.json`/`VERSION` тримає поточну версію бібліотеки.

Цього достатньо для прив'язки «snapshot ↔ platform version».

## Рекомендована цільова архітектура SoT

```
┌────────────────────── creatio-ui repo ─────────────────────┐
│                                                            │
│  TS source ─┬─ @CrtViewElement decorators                  │
│             ├─ ViewConfig interfaces (+ JSDoc)             │
│             └─ @CrtInterfaceDesignerItem (defaults,        │
│                  category hint, propertiesPanel)           │
│                            │                               │
│            tools/component-registry-extractor/             │
│              (нова Nx-таска, ts-morph based)               │
│                            │                               │
│              component-registry.<ver>.json                 │
│                            │                               │
│   Jenkins .pipeline/Jenkinsfile.Release (per release branch)│
│                            │                               │
│            publishes → npm: @creatio/component-registry    │
│                            (semver = platform version)     │
└────────────────────────────┼───────────────────────────────┘
                             │
                             ▼
┌─────────────────────── clio repo ──────────────────────────┐
│                                                            │
│  composer (build-time):                                    │
│    1. npm install @creatio/component-registry@8.1.x \      │
│       @creatio/component-registry@8.2.x …                  │
│    2. diff supported versions → produce one unified        │
│       ComponentRegistry.json with availability ranges      │
│    3. merge ComponentRegistry.overrides.json               │
│       (hand-curated AI hints: aiHidden, replaced-by,       │
│        prefer-converter, deprecation notes)                │
│                            │                               │
│  ships in clio/Command/McpServer/Data/ComponentRegistry.json│
└────────────────────────────────────────────────────────────┘
```

## Етапи екстракції (в `tools/component-registry-extractor/`)

1. **AST walk** усіх `*.ts` із наступними фільтрами.

   **Інклюзивний критерій:** клас із декоратором `@CrtViewElement`. Жодних інших евристик (suffix-фільтрів, name-конвенцій) — це джерело false-negatives.

   **Ексклюзивні фільтри** (всі застосовуються):

   | Категорія | Glob |
   |---|---|
   | Тестові файли | `**/*.spec.ts`, `**/*.spec.ui.ts`, `**/*.spec.tsx`, `**/*.test.ts`, `**/*.test.tsx` |
   | Моки | `**/*.mock.ts`, `**/mocks/**`, `**/__mocks__/**` |
   | Built artifacts | `apps/pkgs/**` |
   | Designer-only UI (lib) | `libs/studio-enterprise/ui/interface-designer-properties-panel/**` |
   | Designer-only UI (subpath) | `**/designtime/**` |
   | Стандартні | `**/node_modules/**`, `dist/**` |

   **Чому `**/designtime/**` потрапив у фільтри:** усі 44 `*PropertiesPanel`-компоненти живуть у subpath `designtime/` всередині своїх lib-папок (розкидано по 27 різних lib — `approval`, `calendar`, `chat-panel`, `compact-profile`, `feed`, `folder-tree`, `message-composer`, ...). Перевірено інваріант на `creatio-ui@master`: 44/44 PropertiesPanel-компонентів — у `**/designtime/**`; 0/192 не-PropertiesPanel — у `**/designtime/**`. Точний і повний filter без false-positive/negative. Прибирає потребу в overlay-rule `aiHidden: true` для 44 рядків.

   **Critical:** парсер `@CrtViewElement` має ігнорувати декоратор всередині JSDoc-коментарів (`/** @CrtViewElement({ type: 'usr.Example' }) */` у файлах декоратор-визначень типу [view-element-registration-config.ts](https://github.com/Advance-Technologies-Foundation/creatio-ui/blob/master/libs/devkit/common/src/lib/public/models/view-element/view-element-registration-config.ts) — інакше підхопить `usr.Example` як псевдо-компонент). Реалізація: strip block- і line-коментарів перед AST-walk, АБО використати ts-morph node-level decorator API замість текстового пошуку.

2. Для кожного класу з `@CrtViewElement`:
   - `componentType` = `decorator.arguments[0].type` (string literal `'crt.X'`).
   - **БЕЗ** semantic-фільтрації по суфіксах. `*PropertiesPanel`, `*Request`-named тощо — включаються в extracted-сет; їх «приховування від AI» — задача `overrides.json` на стороні clio composer (`aiHidden: true`), не extractor-а.
3. Резолвити пов'язаний `ViewConfig` interface:
   - конвенція: `<ComponentName>ViewConfig` у `view-models/` сусіднього файлу, або декларовано через property type / generic.
4. Для кожного property у ViewConfig:
   - `type` — текстова репрезентація TS-типу
   - `description` — JSDoc `@description` або leading коментар
   - `required` — відсутність `?` модифікатора
   - `values` — для union-типів стрічкових літералів
   - `default` — з `@CrtInterfaceDesignerItem.defaultPropertyValues[name]` (компоненто-рівневий декоратор)
   - `availability` — з JSDoc `@since`/`@until`/`@deprecated`
5. Збираємо `category`:
   - JSDoc `@aiCategory containers` overrides все
   - інакше — мапінг через `viewElementGroupType` + конвенції імен файлів (`containers/*` → containers, `widgets/*` → interactive)
6. **Validation step**: запустити runtime registry (опціонально, окремий job) — порівняти AST-видобутий set із зареєстрованим у `BaseViewElementRegistry`. Розбіжності → fail.
7. Output: `component-registry.<version>.json` зі stable schema (та сама форма, що ми вже узгодили: per-entry `availability`).

## JSDoc-вокабуляр у `creatio-ui`

Невелика стандартизована JSDoc-мова (узгодити з UI-командою). Це найдешевший спосіб зробити SoT durable.

```typescript
/**
 * @aiCategory interactive
 * @aiHint "Use crt.ButtonToggleGroup for segmented selection — better UX than multiple buttons."
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

### Теги, які екстрактор має поважати

- `@since <version>` — `availability.since`
- `@deprecated <version> - <reason>` — `availability.until` + `description` suffix
- `@aiCategory <name>` — категорія (containers/fields/interactive/display/filtering)
- `@aiHint "..."` — інлайн-порада для AI

Тег `@aiHidden` **видалено** з вокабуляру extractor-а: оскільки інклюзивний критерій тепер — лише `@CrtViewElement`, без heuristic skip-у по суфіксах, потреби в `@aiInclude` як override-і немає. Приховування «шумних» компонентів (`*PropertiesPanel`, internal table cells, app shell) — задача clio-овського `overrides.json`, не extractor-а. Це чітко розділяє відповідальність: платформа звітує про все, що в неї є; AI-team вирішує, що показувати моделі.

## Шар «overrides» на стороні clio

Авто-видобуток не вирішує AI-специфічні рішення на кшталт:
- «для display-only пошти AI має брати converter, а не `crt.EmailInput`»
- «`crt.SchemaOutlet` низького пріоритету — не пропонувати без explicit user intent»
- хитрі парні правила, які платформа не знає (бо це не платформ-логіка, а AI prompt engineering)

→ окремий файл `ComponentRegistry.overrides.json` у clio, в тому ж форматі, дві операції merge:
- `aiOverlay`: додавати/перевизначати поля (`description`, `aiHint`, `replacedBy`)
- `aiHidden`: «черні» записи, якщо платформа щось експортує, а AI це непотрібно

Merge-порядок: `auto-registry` → накладається `overrides`. Якщо authoritative auto-видобуток виявляє конфлікт (`since` зміщений у новій версії, а override застарів) — composer падає у CI з conflict-report.

## CI flow

### Jenkins у creatio-ui (новий pipeline `Jenkinsfile.Registry`)

1. Тригер: push у release-branch (`8.x.y`) або git-tag.
2. `nx run component-registry-extractor:build` — генерує `component-registry.<version>.json`.
3. Publish artifact: npm пакет `@creatio/component-registry@<version>` у внутрішній npm-registry (вже існує `docker-rnd.creatio.com` екосистема).
4. Optional: post-validation проти runtime registry.

### Composer у clio (новий короткий tool)

Можливі форми: `clio/Command/McpServer/Data/build-registry.cjs` або окрема .NET утиліта.

1. Тригер: вручну / cron в TeamCity / при бампі залежностей / при PR-боті.
2. Pull `@creatio/component-registry@<v1>..@<vN>` для всіх підтримуваних версій (`8.0.x`, `8.1.x`, `8.2.x`).
3. Для кожного `componentType`:
   - якщо присутній у `vN`, відсутній у `vN-1` → `availability.since = vN`
   - якщо був у `vN-1`, зник у `vN` → `availability.until = vN`
   - те ж саме на рівні `properties`
4. Merge `overrides.json`.
5. Output: одна `ComponentRegistry.json` зі повними availability-range.
6. Опційно: відкриває PR у clio через бота.

### TeamCity на стороні clio (якщо це звичний оркестратор)

- Тільки висмикує trigger «composer» — оркеструє запуск pipeline + auto-PR.
- На самому extractor-у нічого не змінює.

### Release branch lifecycle (виділення нової release-лінії)

Базовий тригер `push у release-branch` працює для **вже існуючих** ліній (`8.1.x`, `8.2.x`). Окремо описуємо подію «creatio-ui виділяє нову гілку для майбутнього релізу» — момент, коли `master` (8.4-dev) форкається у `8.3.x` під майбутній GA.

**Політичне рішення:** prerelease-публікацій немає. Внутрішні extractor-build-и на release-branch до GA-тега в npm не публікуються — тільки локальні Jenkins-артефакти.

```
master (8.4 dev) ─┬─ cut → 8.3.x branch created
                  │     │
                  │     ├─ Jenkins on-branch-create (Jenkinsfile.Registry)
                  │     │    ├─ extract @ branch HEAD
                  │     │    ├─ produce baseline component-registry.json як Jenkins artifact
                  │     │    │   (інспекція + регресійний детектор; НЕ npm-publish)
                  │     │    └─ diff vs previous-line GA snapshot → preview звіт у Slack/Jira
                  │     │
                  │     └─ ongoing commits на 8.3.x → re-extract, refresh artifact
                  │          (теж без npm-publish)
                  │
                  └─ tag 8.3.0 (GA) ─ extract → npm publish @creatio/component-registry@8.3.0
                                            └─ manual PR у clio:
                                                supported-versions.json += "8.3.0"
                                                  └─ composer rebuilds unified registry
                                                       └─ auto-PR with new ComponentRegistry.json
```

Етапи:

1. **Branch-cut event у creatio-ui.** Jenkins-pipeline-library має або branch-creation hook, або nightly diff-job, що детектує нові гілки за naming-pattern (`^\d+\.\d+(\.x|\.\d+)?$`). Запускається `Jenkinsfile.Registry` у спеціальному «baseline mode»:
   - extract → archive як build artifact (`component-registry.<branch>.json`);
   - **NO npm publish** — це pre-GA work-in-progress;
   - diff vs остання GA-публікація попередньої лінії → preview-звіт: «що нового з'являється, що зникає, що змінюється в properties».
   - Цей preview ловить регресії в extractor-і та неузгодженості політики (наприклад, помилково знятий `@aiHidden`) **до** GA.

2. **Ongoing commits у release-branch до GA.** Той самий `Jenkinsfile.Registry` re-runs на кожен push, refresh-ить artifact. Композиція в clio не торкається — нової версії у `supported-versions.json` нема.

3. **GA tag.** Push tag `8.3.0` тригерить **публікаційний mode** того ж pipeline:
   - extract → npm publish `@creatio/component-registry@8.3.0`;
   - npm dist-tag `latest` оновлюється тільки якщо це найбільша версія (інакше per-line dist-tag типу `8.3` зберігає сумісність зі старими споживачами).

4. **Реєстрація нової версії на стороні clio (explicit, не auto-discover).**
   - У clio repo живе [supported-versions.json](../clio/Command/McpServer/Data/supported-versions.json) (новий файл) — список minor-ліній, для яких composer тягне snapshot-и:
     ```json
     ["8.0.x", "8.1.x", "8.2.x"]
     ```
   - Додавання нової лінії — manual PR із обґрунтуванням («команда зафіксувала підтримку 8.3.x»).
   - Auto-PR (бот, що моніторить npm registry) — як **опційне** прискорення, але прийняття рішення про підтримку — людське.

5. **Composer-пробіг.** Після bump-у `supported-versions.json` composer (в CI clio) тягне npm-пакети для всього списку, перебудовує unified `ComponentRegistry.json` із оновленим `availability`, відкриває PR.

6. **Branch retirement.** Коли підтримка старої лінії припиняється — PR в clio, що знімає її з `supported-versions.json`. Composer:
   - якщо компонент/property був із цієї лінії і відсутній у наступній — `availability.until` отримує значення першої не-підтримуваної версії;
   - якщо компонент/property присутній у всіх лініях, що залишились — без змін.

**Що дає baseline-mode без публікації:**
- Раннє ловіння extractor-регресій (нова версія платформи з новим декоратор-патерном → padding-у extractor-і).
- Раннє ловіння конфліктів з overlay-ями clio (deprecated в нові версії, але overlay вказує на нього).
- Preview-звіт для AI-команди завчасно (за тижні до GA).

## Що НЕ робити (антипатерни)

- **Не парсити `*.api.md` як JSON.** Це Markdown rollup, призначений для review, не для машинного парсингу. Замість цього — `docModel: true` у api-extractor конфігах, якщо api-extractor буде вторинним джерелом.
- **Не зберігати per-version snapshot файли в clio repo.** Якщо підтримуємо 10 версій — це 10× дублювання, mess у review. Краще — npm-залежності у composer-фазі і ОДИН агрегований файл у clio.
- **Не робити decorator-extraction через regex.** AST-walk через `ts-morph` (TypeScript Compiler API) — єдиний надійний шлях; regex впаде на multi-line, generics, шаблонах.
- **Не блокувати реліз креатіо на extractor.** Build registry окремим job, який падає isolated, не break-ить core deploy pipeline.

## Відкриті питання для команди

1. **Власник `tools/component-registry-extractor/`.** Платформ-команда чи clio/AI-команда? Краще — платформ, ближче до source. Але потребує buy-in.
2. **JSDoc-вокабуляр як стандарт.** Узгодити з UI-командою; пройти через lint-правило (esbuild/eslint custom rule) для обов'язковості при додаванні нового `@CrtViewElement`. Інакше extractor буде fragile.
3. **Хто володіє `ComponentRegistry.overrides.json`.** AI-команда / clio.
4. **Версійна політика подачі AI**: вибираємо «snap до точної версії пакету» чи «matrix підтримки» (наприклад: «AI завжди знає про all entries у minor-line, фільтрує по `build`»).
5. **Внутрішній npm registry** для `@creatio/component-registry` — який саме? Доступний з clio-CI?
6. **Backfill for v8.0/v8.1.** Перший прохід — на історичних branches, чи лише з v8.2 forward (legacy fallback = «availability відсутня = завжди»)?
7. **Що робити з `composable-apps/*`?** У них теж є власні крт-компоненти — це app-level, не platform. Можливо, окремий extractor pass із tag-ом scope в record.
8. **Properties panels.** Поточний кур-registry їх свідомо не містить. Підтверджуємо це як правило і додаємо у фільтр.
9. **Branch-creation hook у Jenkins.** Чи доступний у `@Library('pipeline-library')` готовий тригер на створення нової гілки? Якщо ні — nightly job, що порівнює `git branch -r` із попередньою ітерацією і запускає baseline-extract для нових `^\d+\.\d+(\.x|\.\d+)?$`-branch-ів.
10. **npm dist-tag policy.** `latest` = max-версія (звичайна semver-семантика) чи per-line tag (`8.2`, `8.3`)? Перше простіше, друге стабільніше для споживачів, що пінять mінорну лінію.
11. **Cadence GA-тегів.** Чи кожен build створює GA-тег (`8.3.0`, `8.3.1`, …) — тоді composer ребіл-диться часто; чи тільки на minor-зрізах — composer оновлюється раз у місяць. Впливає на навантаження на clio-CI і частоту auto-PR.
12. **Реєстрація підтримки — людська чи автоматична.** Manual PR у `supported-versions.json` (default) vs auto-PR бота, що пушить, як тільки нова npm-версія з'являється. Manual безпечніше, але потребує дисципліни команди.

## Pilot-план (мінімальний валідаційний цикл, ~2 тижні)

1. Написати `component-registry-extractor` за зразковими 5 компонентами (`crt.Button`, `crt.Input`, `crt.ComboBox`, `crt.FlexContainer`, `crt.TabPanel`).
2. На гілці `8.2.x` дати ему вистрілити локально, порівняти з ручним JSON-ом у clio — як точно AST-видобуток матчить.
3. Додати кілька JSDoc-анотацій (`@since`, `@aiHint`) в одному з ViewConfig — переконатися, що екстрактор їх правильно тягне.
4. Поексперементувати з composer-merge на 2 версіях (`8.1.x` + `8.2.x`) і подивитися на згенерований `availability`-діапазон.
5. Якщо результат сходиться з ручним — підв'язати у Jenkins для однієї бранчі, потім масштабувати.

Це дешевий gate-test: пілот покаже, чи варто інвестувати у full pipeline, до того, як питання pipeline-оркестрації стане blocking.

## Артефакти, які вже існують у creatio-ui (готова інфра)

- **api-extractor конфіги** у `libs/devkit/common`, `libs/devkit/base`, `libs/devkit/interface-designer` — `apiReport.enabled: true`, `docModel: false` (`.api-extractor/*.api.md` вже генерується).
- **Nx monorepo** — `nx.json`, `project.json` у кожному lib — стандартний шлях для нової `tools/component-registry-extractor` таски.
- **Jenkins pipeline-library** — `@Library('pipeline-library')` патерн, використовується у `Jenkinsfile.Release`. Готова інфра для додавання Registry pipeline.
- **Docker-rnd registry** (`docker-rnd.creatio.com/monorepo-worker:1.0.12`) — внутрішня екосистема публікації артефактів.
