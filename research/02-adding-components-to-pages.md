# Як AI додає компоненти на Freedom UI сторінку

Детальний flow модифікації сторінок через MCP. Джерело правди про правила — [PageModificationGuidanceResource.cs](../clio/Command/McpServer/Resources/PageModificationGuidanceResource.cs); реалізація — [PageUpdateTool.cs](../clio/Command/McpServer/Tools/PageUpdateTool.cs).

## Інструменти, які беруть участь

| Tool | Призначення |
|---|---|
| `list-pages` | Знайти точне `schema-name` сторінки (за UId з URL, фільтром, тощо) |
| `get-page` | Прочитати поточний стан: `raw.body`, `bundle.containers`, `bundle.viewConfig`, `page.ownBodySummary` |
| `get-component-info` | Курований каталог Freedom UI компонентів із готовими прикладами `insert` |
| `get-guidance` | Завантажити правила: `page-modification`, `page-schema-converters`, `page-schema-handlers`, `page-schema-validators`, `page-schema-creatio-devkit-common` |
| `validate-page` | Dry-run без збереження |
| `update-page` | Зберегти зміни (новий компонент іде сюди) |
| `sync-pages` | Те ж саме, але батчево для багатьох сторінок |

## Канонічний flow

1. **`list-pages`** → `schema-name` сторінки, яку правимо.
2. **`get-page schema-name=…`** → отримуємо `bundle.containers` (плоский список валідних `parentName`), `raw.body` (поточне тіло) і `page.ownBodySummary`.
3. **Вибрати контейнер-батько** з `bundle.containers` — за `type` (`crt.FlexContainer`, `crt.Grid`, `crt.TabContainer` …) та `childCount > 0`, ім'я → `parentName`.
4. **(Якщо змінюємо відображення)** Спочатку `get-guidance name=page-schema-converters` — інакше AI помилково обере компонент замість конвертера (`crt.EmailInput` замість `crt.ToEmailLink` тощо).
5. **`get-component-info component-type=crt.Button`** → готовий приклад `insert`-операції.
6. **Скласти МІНІМАЛЬНЕ тіло** з тільки новими операціями всередині 6 обов'язкових маркерів:
   - `SCHEMA_DEPS`, `SCHEMA_ARGS`, `SCHEMA_VIEW_CONFIG_DIFF`, `SCHEMA_VIEW_MODEL_CONFIG_DIFF`, `SCHEMA_MODEL_CONFIG_DIFF`, `SCHEMA_HANDLERS`, `SCHEMA_CONVERTERS`, `SCHEMA_VALIDATORS`.
7. **`update-page schema-name=… body=… mode=append`** — `mode=append` мерджить твій фрагмент із поточним body на сервері:
   - `viewConfigDiff` — dedupe по `name`
   - `handlers` — dedupe по `request`
   - `converters` — merge по ключу
   - `viewModelConfigDiff` / `modelConfigDiff` — plain concat
8. (Опційно) `verify:true` — повертає метадані після save.

## Приклад тіла для додавання кнопки з handler-ом

```javascript
define("<PageName>", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ {
    return {
        viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[{
            "operation": "insert",
            "name": "UsrMyButton",
            "values": {
                "type": "crt.Button",
                "caption": "My button",
                "clicked": { "request": "usr.MyClickRequest" }
            },
            "parentName": "FilterGridContainer",
            "propertyName": "items",
            "index": 0
        }]/**SCHEMA_VIEW_CONFIG_DIFF*/,
        viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/,
        modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/,
        handlers: /**SCHEMA_HANDLERS*/[{
            request: "usr.MyClickRequest",
            handler: async (request) => {
                alert("My button clicked");
                return request.next?.handle(request);
            }
        }]/**SCHEMA_HANDLERS*/,
        converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/,
        validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/
    };
});
```

## Що саме можна додавати

- **`viewConfigDiff` операції**: `insert`, `remove`, `merge`, `move` — це і є додавання/переміщення/видалення компонентів.
- **`viewModelConfigDiff`** — атрибути в'юмоделі (валідатори, sortingConfig для DataGrid).
- **`modelConfigDiff`** — біндинги до entity моделі.
- **`handlers`** — JS-обробники запитів (`usr.*` повинні мати парний handler; `crt.*` лише при override).
- **`converters`** — функції трансформації значень для відображення.
- **`validators`** — кастомні правила валідації.

## Replacing-schema концепція

Коли Freedom UI designer зберігає зміни до сторінки, Creatio створює **replacing schema** у "design package". Replacing schema успадковує оригінал і містить тільки diff.

- Design package ≠ пакет, якому належить оригінальна schema.
- `get-page` та `update-page` автоматично резолвлять replacing schema через design package.
- Редагований target — завжди `hierarchy[0]` (наймолодша derived schema).
- Якщо replacing schema ще не існує, `hierarchy[0]` = оригінал, design package = оригінальний пакет.

## get-page response structure

- `page` — метадані editable replacing schema: `schemaName`, `schemaUId`, `packageName`, `packageUId`, `parentSchemaName`.
- `raw.body` — повне JS-тіло replacing schema (з маркерами). Read-only reference.
- `bundle` — read-only merged view по всій ієрархії; **не слати як body payload**.
  - `viewConfig` — масив-контейнер навколо merged root (`viewConfig[0]` — корінь).
  - `containers` — плоский масив `{ name, type, childCount, path }`. Використовувати `.containers[]`.
  - `handlers`/`converters`/`validators` — raw JS source strings, не parsed JSON.

## Ключові правила (на що AI «накручений» через guidance-resources)

### НЕ слати `raw.body` назад верба́тим

`raw.body` містить вже існуючі merge-операції. Re-send → backend знову застосовує їх до батьківської ієрархії → typical fail `"The requested operation requires an element of type 'Object', but the target element has type 'Array'"`.

Правильний паттерн: **мінімальне body** з тільки новими операціями.

Sanity check: якщо `page.ownBodySummary.viewConfigDiffOperations > 1` або `ownBodySummary.bodyLength > 1000` — посилати тільки delta.

### `parentName` не вигадувати

Брати тільки з `bundle.containers`. Перевагу — `childCount > 0` (існуючий sibling підтверджує валідність контейнера).

Поширені типи контейнерів: `crt.FlexContainer` (filter rows, action bars), `crt.Grid`, `crt.TabContainer`, `crt.Expansion`.

### Append vs Replace mode

- `mode: "replace"` (default) — body заміщає schema body verbatim. Тільки для composing-from-scratch.
- `mode: "append"` — clio мерджить incoming фрагмент із поточним body на сервері. Безпечно для додавання до існуючих кастомних сторінок.
- В append-mode дозволено passing тільки змінювані секції (наприклад тільки `SCHEMA_VIEW_CONFIG_DIFF` + `SCHEMA_HANDLERS`). Відсутні секції не торкаються.

### Маркери

Дозволені: `SCHEMA_DEPS`, `SCHEMA_ARGS`, `SCHEMA_VIEW_CONFIG_DIFF`, `SCHEMA_VIEW_MODEL_CONFIG_DIFF`, `SCHEMA_MODEL_CONFIG_DIFF`, `SCHEMA_HANDLERS`, `SCHEMA_CONVERTERS`, `SCHEMA_VALIDATORS`.

Не вигадувати кастомні маркери (наприклад `SCHEMA_WRAPPERS` не існує).

### Замовчування для нових компонентів

- `viewConfigDiff[].name` — унікальний id; з префіксом `Usr` для кастомних.
- Для entity-bound FormPage полів `control` біндинг → `$PDS_<ColumnName>`.
- Match column DataValueType ↔ control type:
  - `ShortText`/`MediumText`/`LongText` → `crt.Input`
  - `Lookup` → `crt.ComboBox`
  - `Boolean` → `crt.Checkbox`
  - `DateTime`/`Date`/`Time` → `crt.DateTimePicker`
  - `Integer`/`Float`/`Money` → `crt.NumberInput`
  - `Email` → `crt.EmailInput`
  - `PhoneNumber` → `crt.PhoneInput`
  - `WebLink` → `crt.WebInput`

### Handlers — це raw JavaScript

Між `/**SCHEMA_HANDLERS*/` маркерами — raw JS, не JSON. Unquoted keys (`request`, `handler`), arrow functions або function expressions.

Кожен `clicked.request` з namespace `usr.*` (або кастомним) **МАЄ** мати парний handler entry з тим самим `request` string.

Завжди завершувати кастомний handler `return request.next?.handle(request);` для пропагування у default pipeline.

`crt.*` requests мають default handlers — не дублювати, якщо не override.

### Не потрібно `compile-creatio`

Freedom UI body — AMD module, served at runtime. `update-page` НЕ потребує наступного `compile-creatio`.

## Replacing-schema і design package

- `get-page` повертає `page.designPackageUId` — пакет, куди `update-page` збереже.
- `page.willCreateReplacingInDesignPackage: true` означає, що НОВА replacing schema буде матеріалізована на save.
- Backend резолвить design package детерміновано від locked schema's owning app через `SysPackageInInstalledApp`.
- **Не передавати `target-package-uid` у нормальних flow** — backend сам резолвить через `GetDesignPackageUId`.

## Відомі обмеження

- `update-page` fail-closed на design-package resolution: якщо `GetDesignPackageUId` падає — error замість silent fallback.
- `get-page` робить best-effort fallback до оригіналу для read.
- Replacing schemas поза design package не видно через `GetDesignPackageUId` — використовувати `list-pages` filter by name.
- Handler block парситься тільки Acorn-ом; семантичні помилки (wrong argument names, missing await) виявляться лише в браузері.
- ListPage DataGrid sorting: використовувати `viewModelConfigDiff` через `Items.modelConfig.sortingConfig.attributeName`. Не вставляти `viewConfig.sorting` чи `viewConfig.sortingChange` вручну — frontend preprocessor auto-injects з `sortingConfig`.
