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

## Авто-екстракція з `creatio-ui` (актуалізовано)

Уточнений критерій для extractor-а:

**Інклюзивний фільтр** — клас із декоратором `@CrtViewElement` (TS source у репо [creatio-ui](https://github.com/Advance-Technologies-Foundation/creatio-ui)).

**Ексклюзивні фільтри** (паралельні, всі застосовуються):
- тестові файли: `**/*.spec.ts`, `**/*.spec.ui.ts`, `**/*.spec.tsx`, `**/*.test.ts`, `**/*.test.tsx`
- моки: `**/*.mock.ts`, `**/mocks/**`, `**/__mocks__/**`
- папка `apps/pkgs/**` (built artifacts, не source)
- папка `libs/studio-enterprise/ui/interface-designer-properties-panel/**` (designer-only UI)
- subpath `**/designtime/**` (designer config panels — всі 44 `*PropertiesPanel`-компоненти розкидані по lib-папках, але строго у `designtime/`)
- стандартні: `**/node_modules/**`, `dist/**`

**Технічно важливо:** парсер має ігнорувати `@CrtViewElement` всередині коментарів (JSDoc-приклади в файлах декоратор-визначень), інакше підхопить `usr.Example` як «псевдо-компонент».

### Цифри станом на `creatio-ui@master`

| Сегмент | Кількість |
|---|---|
| Усі `@CrtViewElement` після ексклюзій | **192** |
| З них уже у ручному `ComponentRegistry.json` | 92 (повна перетинка) |
| Нові кандидати | 100 |
| `*PropertiesPanel` залишилось у наборі | 0 (відсіяно `**/designtime/**`) |

**Регресія:** жоден компонент із поточних 92 курованих НЕ зникає з extracted-сету. Авто-каталог — суперсет ручного.

**Чому `**/designtime/**` — точний фільтр:** 44/44 `*PropertiesPanel`-компонентів — у `**/designtime/**`; 0/192 не-PropertiesPanel — у `**/designtime/**`. Жодного false-positive, жодного false-negative.

### 100 нових кандидатів (треба ревью AI-команди)

Без додаткової семантичної фільтрації — щоб команда побачила весь шум і вирішила, що включати:

```
crt.ActionDashboard              crt.HtmlCodeEditor
crt.AgentInbox                   crt.Icon
crt.AllowedResults               crt.IncomingItem
crt.Angular7XDetail              crt.IncomingItems
crt.AppBackground                crt.ItemWrapper
crt.AppToolbar                   crt.List
crt.ApprovalTile                 crt.LookupQuickFilterMenuItem
crt.ArticlePreview               crt.MessageComposerSelector
crt.ArticlesList                 crt.MessageEditor
crt.AutoTranslateToggle          crt.MessageEditorBody
crt.AutoTranslateToggleButton    crt.MessageEditorInput
crt.BaseMessageComposer          crt.MessageEditorReply
crt.BaseMessageComposerSkeleton  crt.ModuleLoader
crt.BaseTimelineLabel            crt.NavigationPanel
crt.ButtonToggleGroupItem        crt.NavigationPanelItem
crt.CallConversation             crt.NextBestOffer
crt.CampaignViewer               crt.NextBestOfferItem
crt.ChannelSelector              crt.NextStepTile
crt.ChatAttachmentTile           crt.ObjectExplorer
crt.ChatComposer                 crt.OperatorState
crt.ChatDisclaimer               crt.RichTextLinkComponent
crt.ChatItem                     crt.RichTextVideoComponent
crt.ChatList                     crt.RouterOutlet
crt.ChatQuickActions             crt.SequenceGalleryItemConfig
crt.ChatTransferSelect           crt.SkipLinks
crt.ChatTransferSelectList       crt.StepBuilder
crt.ChatTyping                   crt.Switch
crt.ComboboxAction               crt.TabPanelHeader
crt.ComponentList                crt.TabPanelHeaderItem
crt.ContactProfilePanel          crt.TableBooleanCell
crt.ConversationTemplateList     crt.TableColoredCell
crt.DataTableEditDateTimeCell    crt.TableDateTimeCell
crt.DataTableEditEmailCell       crt.TableDcmStageCell
crt.DataTableEditLookupCell      crt.TableDcmStageEditingCell
crt.DataTableEditNumericCell     crt.TableEmailCell
crt.DataTableEditPhoneCell       crt.TableFileCell
crt.DataTableEditTextCell        crt.TableFileSizeCell
crt.DataTableEditWebCell         crt.TableNumericCell
crt.DeprecatedInput              crt.TablePhoneCell
crt.DeprecatedLabel              crt.TableRichTextEditorCell
crt.Description                  crt.TableSliderCell
crt.EditTypedValueCell           crt.TableTextCell
crt.EmailComposer                crt.TemplateGallery
crt.EmojiSelect                  crt.TemplateGalleryItem
crt.EntityIcon                   crt.TemplateList
crt.FileGalleryItem              crt.TemplateSelect
crt.FilterableList               crt.Timer
                                 crt.TranslateToggle
                                 crt.TranslateToggleButton
                                 crt.TypedValueCell
                                 crt.WaterfallWidget
                                 crt.WebChatPreview
                                 crt.WebTextInput
```

Видно три природні підкатегорії «шуму», які можна позначати `@aiHidden` або винести в окремий scope:
- **Internal table cells**: `crt.DataTableEdit*Cell`, `crt.Table*Cell`, `crt.EditTypedValueCell`, `crt.TypedValueCell` — рендеряться `crt.DataGrid` сам, AI не повинен їх інстанціювати напряму.
- **App shell / infra**: `crt.AppBackground`, `crt.AppToolbar`, `crt.NavigationPanel*`, `crt.RouterOutlet`, `crt.ModuleLoader`, `crt.LazyElement`, `crt.SkipLinks` — каркасні елементи, не для бізнес-сторінок.
- **Deprecated / legacy**: `crt.DeprecatedInput`, `crt.DeprecatedLabel`, `crt.Angular7XDetail`, `crt.BaseTimelineLabel`, `crt.BaseMessageComposer*` — або застаріле, або base-класи без самостійного UX.

**Реальні нові кандидати для каталогу** (після відсіювання трьох груп вище): ~60 компонентів, зокрема `crt.ButtonToggleGroupItem`, `crt.ChatComposer`, `crt.ChatList`, `crt.EmailComposer`, `crt.EmojiSelect`, `crt.HtmlCodeEditor`, `crt.Icon`, `crt.NextBestOffer`, `crt.Switch`, `crt.TabPanelHeader`, `crt.TemplateGallery`, `crt.TemplateSelect`, `crt.Timer`, `crt.TranslateToggle`, `crt.WaterfallWidget`, `crt.WebTextInput` — це список для перевірки і додавання у ручний overlay або експозиції через extractor.

### Designer config panels (`*PropertiesPanel`) — відсіяно фільтром

44 `*PropertiesPanel`-компоненти (designer-конфігурація властивостей у правій панелі Interface Designer-а) розкидані по 27 lib-папках (`approval`, `calendar`, `chat-panel`, `compact-profile`, `feed`, `folder-tree`, `message-composer`, `timeline`, ...), але всі строго у subpath `**/designtime/**`. Цей glob додано до extractor-ексклюзій → жоден `*PropertiesPanel` не потрапляє в extracted-сет, потреби в `aiHidden: true` overlay-правилах для них немає.

## Спостереження для версіонування

- Поточний registry **не має жодного поля версії** — ні на рівні компонента, ні на рівні property.
- Жодного API для запиту «що актуально для платформи 8.1.5» — інструмент завжди повертає той самий статичний набір.
- Категорії захардкоджені у двох місцях ([ComponentInfoCatalog.cs:40](../clio/Command/McpServer/Tools/ComponentInfoCatalog.cs#L40), [ComponentInfoTool.cs:17](../clio/Command/McpServer/Tools/ComponentInfoTool.cs#L17)) — `CategoryOrder = ["containers","fields","interactive","display"]` + `"filtering"` тримається в даних.

Через що приходимо до потреби в structurі, описаній у [04-multi-version-target-structure.md](04-multi-version-target-structure.md).
