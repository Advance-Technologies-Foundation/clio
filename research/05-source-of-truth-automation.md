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

- **324** `@CrtViewElement` декорувань (без `*.spec.ts`)
- **92** записи у курованому JSON

Більшість 324 — це `*PropertiesPanel`, `crt.*Request`, `crt.*Converter`. Ручний registry свідомо звужено до AI-релевантних UI-компонентів. Це треба формалізувати у **фільтр-правилі** (інакше автогенерація поверне «шум»).

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

1. **AST walk** усіх `*.ts` в `libs/` (виключно `**/*.spec.ts`, `**/node_modules/**`).
2. Для кожного класу з `@CrtViewElement`:
   - `componentType` = `decorator.arguments[0].type` (string literal `'crt.X'`).
   - `aiHidden`: якщо JSDoc на класі/декораторі має `@aiHidden`, або тип містить `PropertiesPanel|Request|Converter|Handler` — пропустити (override: explicit `@aiInclude`).
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
- `@aiHidden` — не експортувати в registry
- `@aiInclude` — explicit override на heuristic skip
- `@aiHint "..."` — інлайн-порада для AI

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
