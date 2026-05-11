# Поточний каталог Freedom UI компонентів, доступних AI

92 компоненти в 5 категоріях. Зашиті у [ComponentRegistry.json](../clio/Command/McpServer/Data/ComponentRegistry.json), доступні через MCP-tool `get-component-info` (`component-type=list` або `search=…`).

Контракт запису ([ComponentInfoTool.cs:252](../clio/Command/McpServer/Tools/ComponentInfoTool.cs#L252)):

```csharp
public sealed class ComponentRegistryEntry {
    public string ComponentType { get; init; }            // "crt.Button"
    public string Category { get; init; }                  // "interactive"
    public string Description { get; init; }
    public bool Container { get; init; }
    public IReadOnlyList<string> ParentTypes { get; init; }
    public IReadOnlyDictionary<string, ComponentPropertyDefinition> Properties { get; init; }
    public IReadOnlyList<string> TypicalChildren { get; init; }
    public JsonElement? Example { get; init; }
}
```

## containers (10) — макет

- `crt.TabPanel`, `crt.TabContainer` — табові панелі
- `crt.FlexContainer`, `crt.GridContainer`, `crt.HeaderContainer` — макетні контейнери
- `crt.ExpansionPanel`, `crt.ToggleContainer`, `crt.ToggleContainerItem` — згортувані секції
- `crt.SidebarContainer` — бічна панель
- `crt.SchemaOutlet` — інлайн-вбудовування іншої схеми

## fields (19) — поля вводу, прив'язані до entity моделі

- `crt.Input`, `crt.NumberInput`, `crt.Checkbox`, `crt.DateTimePicker`, `crt.ComboBox`
- `crt.EmailInput`, `crt.PhoneInput`, `crt.WebInput`, `crt.PasswordInput`, `crt.EncryptedInput`
- `crt.RichTextEditor`, `crt.HtmlEditor`
- `crt.ColorPicker`, `crt.Slider`, `crt.IconRadioButton`
- `crt.ImageInput`, `crt.FileInput`
- `crt.MultiSelect`, `crt.TagSelect`

## interactive (38) — складні віджети та інтерактив

### Дії
- `crt.Button`, `crt.Menu`, `crt.MenuItem`, `crt.ButtonToggleGroup`, `crt.ComboboxSearchTextAction`

### Списки / таблиці
- `crt.DataGrid`, `crt.MultiList`, `crt.Gallery`, `crt.FileList`, `crt.FileDrop`

### Дашборди / віджети
- `crt.Dashboards`, `crt.ChartWidget`, `crt.IndicatorWidget`, `crt.GaugeWidget`, `crt.ListWidget`, `crt.FunnelWidget`, `crt.FullPipelineWidget`, `crt.SalesWaterfallWidget`

### Таймлайн
- `crt.Timeline`, `crt.TimelineTile`

### Спілкування
- `crt.Chat`, `crt.Conversation`, `crt.OmnichannelInbox`, `crt.Feed`, `crt.FeedItem`, `crt.FeedComposer`, `crt.CommunicationOptions`

### CRM-специфіка
- `crt.Approval`, `crt.ApprovalList`, `crt.NextSteps`, `crt.Playbook`, `crt.EntityStageProgressBar`, `crt.Calendar`

### Картки профілів
- `crt.AccountCompactProfile`, `crt.ContactCompactProfile`, `crt.UserCompactProfile`

### Інше
- `crt.IFrame`, `crt.FormRules`

## display (17) — read-only відображення

- `crt.Label`, `crt.Link`, `crt.Badge`, `crt.LocalTime`, `crt.Placeholder`
- `crt.Chip`, `crt.ChipList`
- `crt.Summaries`, `crt.SummaryItem`
- `crt.FilePreview`
- **Меню-декор**: `crt.MenuDivider`, `crt.MenuLabel`
- **Timeline-лейбли**: `crt.TimelineLabel`, `crt.TimelineEmailLabel`, `crt.TimelinePhoneLabel`, `crt.TimelineWebLabel`, `crt.TimelineLookup`

## filtering (8) — фільтри для списків

- `crt.QuickFilter`, `crt.SearchFilter`
- `crt.FilterBuilderSource`, `crt.FilterBuilderToggler`, `crt.FiltersContainer`
- `crt.FolderTree`, `crt.FolderTreeActions`, `crt.EntityHierarchyFilter`

## Що повертає `get-component-info component-type=crt.X`

Для кожного типу:
- `description` — короткий опис призначення
- `parentTypes` — у які контейнери компонент валідно вкладати
- `typicalChildren` — типові дочірні елементи (для контейнерів)
- `properties` — повний контракт `values.*` із описом, типом і допустимими значеннями (`required`, `default`, `values`)
- Готовий приклад `insert`-операції для `viewConfigDiff`

Це і є той «довідник», на який AI спирається при складанні body для `update-page`.

## Спостереження для версіонування

- Поточний registry **не має жодного поля версії** — ні на рівні компонента, ні на рівні property.
- Жодного API для запиту «що актуально для платформи 8.1.5» — інструмент завжди повертає той самий статичний набір.
- Категорії захардкоджені у двох місцях ([ComponentInfoCatalog.cs:40](../clio/Command/McpServer/Tools/ComponentInfoCatalog.cs#L40), [ComponentInfoTool.cs:17](../clio/Command/McpServer/Tools/ComponentInfoTool.cs#L17)) — `CategoryOrder = ["containers","fields","interactive","display"]` + `"filtering"` тримається в даних.

Через що приходимо до потреби в structurі, описаній у [04-multi-version-target-structure.md](04-multi-version-target-structure.md).
