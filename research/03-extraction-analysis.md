# Extractor: фільтр, інваріанти, кандидати

Реальні цифри і список «шуму» для імплементації `tools/component-registry-extractor/` у [creatio-ui](https://github.com/Advance-Technologies-Foundation/creatio-ui). Документ підтверджує, що інклюзивний критерій `@CrtViewElement` плюс ексклюзивні folder-фільтри дають детерміністичний superset поточного ручного `ComponentRegistry.json`.

## Критерії фільтрації

**Інклюзивний фільтр** — клас із декоратором `@CrtViewElement`.

**Ексклюзивні фільтри** (паралельні, всі застосовуються):
- тестові файли: `**/*.spec.ts`, `**/*.spec.ui.ts`, `**/*.spec.tsx`, `**/*.test.ts`, `**/*.test.tsx`
- моки: `**/*.mock.ts`, `**/mocks/**`, `**/__mocks__/**`
- папка `apps/pkgs/**` (built artifacts, не source)
- папка `libs/studio-enterprise/ui/interface-designer-properties-panel/**` (designer-only UI)
- subpath `**/designtime/**` (designer config panels — всі 44 `*PropertiesPanel`-компоненти розкидані по lib-папках, але строго у `designtime/`)
- стандартні: `**/node_modules/**`, `dist/**`

**Технічно важливо:** парсер має ігнорувати `@CrtViewElement` всередині коментарів (JSDoc-приклади у файлах декоратор-визначень), інакше підхопить `usr.Example` як «псевдо-компонент».

## Цифри станом на `creatio-ui@master`

| Сегмент | Кількість |
|---|---|
| Усі `@CrtViewElement` після ексклюзій | **192** |
| З них уже у ручному `ComponentRegistry.json` | 92 (повна перетинка) |
| Нові кандидати | 100 |
| `*PropertiesPanel` залишилось у наборі | 0 (відсіяно `**/designtime/**`) |

**Регресія:** жоден компонент із поточних 92 курованих НЕ зникає з extracted-сету. Авто-каталог — суперсет ручного.

**Чому `**/designtime/**` — точний фільтр:** 44/44 `*PropertiesPanel`-компонентів — у `**/designtime/**`; 0/192 не-PropertiesPanel — у `**/designtime/**`. Жодного false-positive, жодного false-negative.

## 100 нових кандидатів (треба ревью AI-команди)

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

Видно три природні підкатегорії «шуму», які composer-овський `overrides.json` має приховати через `aiHidden`:

- **Internal table cells**: `crt.DataTableEdit*Cell`, `crt.Table*Cell`, `crt.EditTypedValueCell`, `crt.TypedValueCell` — рендеряться `crt.DataGrid` сам, AI не повинен їх інстанціювати напряму.
- **App shell / infra**: `crt.AppBackground`, `crt.AppToolbar`, `crt.NavigationPanel*`, `crt.RouterOutlet`, `crt.ModuleLoader`, `crt.LazyElement`, `crt.SkipLinks` — каркасні елементи, не для бізнес-сторінок.
- **Deprecated / legacy**: `crt.DeprecatedInput`, `crt.DeprecatedLabel`, `crt.Angular7XDetail`, `crt.BaseTimelineLabel`, `crt.BaseMessageComposer*` — або застаріле, або base-класи без самостійного UX.

**Реальні нові кандидати для каталогу** (після відсіювання трьох груп вище): ~60 компонентів, зокрема `crt.ButtonToggleGroupItem`, `crt.ChatComposer`, `crt.ChatList`, `crt.EmailComposer`, `crt.EmojiSelect`, `crt.HtmlCodeEditor`, `crt.Icon`, `crt.NextBestOffer`, `crt.Switch`, `crt.TabPanelHeader`, `crt.TemplateGallery`, `crt.TemplateSelect`, `crt.Timer`, `crt.TranslateToggle`, `crt.WaterfallWidget`, `crt.WebTextInput` — це список для перевірки і додавання у ручний overlay або експозиції через extractor.

## Designer config panels (`*PropertiesPanel`) — відсіяно фільтром

44 `*PropertiesPanel`-компоненти (designer-конфігурація властивостей у правій панелі Interface Designer-а) розкидані по 27 lib-папках (`approval`, `calendar`, `chat-panel`, `compact-profile`, `feed`, `folder-tree`, `message-composer`, `timeline`, ...), але всі строго у subpath `**/designtime/**`. Цей glob додано до extractor-ексклюзій → жоден `*PropertiesPanel` не потрапляє в extracted-сет, потреби в `aiHidden: true` overlay-правилах для них немає.

## Звідки взяті ці фільтри і цифри

Емпіричні дані з прохода по `creatio-ui@master` (попередня research-сесія). Розширений контекст архітектури — [05-source-of-truth-automation.md](05-source-of-truth-automation.md); цільова структура реєстру в clio — [04-multi-version-target-structure.md](04-multi-version-target-structure.md).
