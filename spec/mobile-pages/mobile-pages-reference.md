# Mobile Freedom UI Pages — Verified Source of Truth

> **Audience**: AI agents (clio MCP) creating or editing mobile Freedom UI page schemas.
>
> **Last verified**: 2026-05-18
>
> **Verification sources**: clio source code (C# — `SchemaValidationService`, `PageUpdateCommand`, `PageSchemaBodyParser`, `MobileComponentRegistry.json`), `CrtUIPlatform` package schemas (PackageStore), Creatio Academy, Creatio 8.x live environment.

---

## 1. Schema Identity

A schema is a mobile Freedom UI page when:

| Property | Value |
|---|---|
| `schemaType` (numeric) | `10` (`ClientUnitSchemaType.MobileSchema`) |
| `schemaGroup` | `MobilePage` |
| Designer route | `MobilePageDesigner` |
| Runtime | Native mobile app (iOS / Android) |
| Available since | Creatio 8.3.2 |

Web Freedom UI pages use `schemaType = 9` (`AngularSchema`) and `schemaGroup = Page`.

---

## 2. Template Hierarchy

All templates live in the `CrtUIPlatform` package. Two independent root schemas; neither is the parent of the other.

```
BlankMobilePageTemplate  (478ab83b-527b-4830-b2b8-2206bb9bf283)
    Standalone root. Bare crt.Scaffold with empty leading/actions/items.

BaseMobileTemplate       (1d1942c9-6993-41c6-9eda-7b697acca221)
    Separate root. Adds $PageTitle and a CloseButton to the Scaffold,
    plus a MainContainer (crt.GridContainer).
    ├── BaseMobilePageTemplate  (a6a776d7-4415-4726-bf63-a1f19edef6e7)
    │       Record / form pages.
    │       Adds: CancelButton, SaveButton, FloatingActionButton (copy/delete menu),
    │             AreaProfileContainer (crt.GridContainer, color=primary).
    │       └── MobilePageWithTabsFreedomTemplate  (019c694a-1a3b-4812-b2e5-219b326061ae)
    │               Tabbed record pages.
    │               Adds: crt.TabPanel with GeneralInfoTab, FeedTab, AttachmentsTab.
    └── BaseMobileListTemplate  (011e7dda-a763-4535-9b9a-e09eddd047be)
            List / section pages.
            Adds: search button, FloatingActionButton (create record),
                  HeaderContainer (filters/sort/folder tree),
                  ListContainer with crt.List bound to $Items.
```

### Typical template use cases

| Use case | Template |
|---|---|
| Custom / blank page | `BlankMobilePageTemplate` |
| Simple page with title bar (no record ops) | `BaseMobileTemplate` |
| Record (form) page | `BaseMobilePageTemplate` |
| Record page with tabs, feed, attachments | `MobilePageWithTabsFreedomTemplate` |
| List / section page | `BaseMobileListTemplate` |

---

## 3. Schema Body Format

Mobile page bodies are **plain JSON** — not AMD `define(...)` JavaScript modules.

```json
{
  "viewConfigDiff": [],
  "viewModelConfigDiff": [],
  "modelConfigDiff": []
}
```

**No other top-level sections exist in mobile page bodies.** `handlers`, `converters`, and `validators` are web-only (AMD) sections. The clio validation pipeline (`PageSchemaBodyParser`) rejects mobile bodies that contain them.

OOTB converters can be *referenced* as inline binding expressions within `viewConfigDiff` values — for example, `"visible": "$HasUnsavedData | crt.InvertBooleanValue"` or `"visible": "$CardState | crt.IsEqual : 'edit'"`. These are expression strings evaluated by the mobile runtime, not entries in a `converters` section.

### Section semantics

| Section | Purpose |
|---|---|
| `viewConfigDiff` | Visual tree operations: add/merge/move/remove UI elements |
| `viewModelConfigDiff` | View-model patches: attributes, bindings, resource strings |
| `modelConfigDiff` | Data-source patches: `primaryDataSourceName`, `dataSources`, dependencies |

---

## 4. `crt.Scaffold` — Root Element

All five verified mobile templates insert exactly one `crt.Scaffold` at the top level of `viewConfigDiff`. All other content goes inside it. Consumer pages inherit the Scaffold from their parent template.

```json
{
  "operation": "insert",
  "name": "Scaffold",
  "values": {
    "type": "crt.Scaffold",
    "title": "$PageTitle",
    "leading": [],
    "actions": [],
    "floatAction": null,
    "items": []
  },
  "index": 0
}
```

| Scaffold property | Purpose |
|---|---|
| `title` | Page title (typically `"$PageTitle"` attribute reference) |
| `leading` | Left-side navigation items (back/cancel buttons) |
| `actions` | Right-side action items (save button, search button) |
| `floatAction` | Single floating action button object (`crt.FloatingActionButton`) |
| `items` | Page body — array of container/component references |
| `header` | Header child components |
| `fullScreen` | Full-screen mode (boolean) |
| `useSurface` | Use surface background color (boolean) |
| `leadingWidth` | Leading area width (number) |

---

## 5. Differences from Web Pages

Verified from creatio-ui mobile page designer features service:

| Feature | Web | Mobile |
|---|---|---|
| Multi-data-source | Enabled | **Disabled in designer** (`disableMultiDataSource: true`) |
| Masked fields | Supported | **Not supported** (`isMaskedPropertyVisible = false`) |
| Unsupported data types | — | `SECURE_TEXT`, `Color`, `FILE` |
| Body format | AMD JS module | **Plain JSON** |
| Runtime | Browser | Native mobile app |
| Schema group | `Page` (and others) | `MobilePage` |
| Related pages addon | `AddonName.RelatedPage` | `AddonName.MobileRelatedPage` |
| Page properties panel | `crt.PagePropertiesPanel` | `crt.MobilePagePropertiesPanel` |
| Component registries | Web registries | **Separate mobile registries** |

A component type like `crt.Button` exists in both registries but may have different available properties. Web-only components are not rendered by the mobile runtime.

---

## 6. Adaptive Breakpoints

Mobile pages support 3 viewport breakpoints, defined in `MobileBreakpointType`:

| Value | Viewport |
|---|---|
| `"small"` | Phone portrait — default canvas in designer |
| `"medium"` | Phone landscape / tablet portrait |
| `"large"` | Tablet landscape |

`crt.GridContainer` supports an `adaptive` property for per-breakpoint column overrides:

```json
{
  "type": "crt.GridContainer",
  "columns": "1fr",
  "adaptive": {
    "small": { "columns": "1fr" },
    "medium": { "columns": "1fr 1fr" },
    "large": { "columns": "1fr 1fr 1fr" }
  }
}
```

---

## 7. Available Components (Category Overview)

Mobile components are registered through mobile-specific decorators (`@CrtMobileViewElement`, `@CrtMobileInterfaceDesignerItem`) — separate from the web registry. Only components registered for mobile are valid in a mobile page.

**Input / form**
`crt.Input`, `crt.NumberInput`, `crt.EmailInput`, `crt.WebInput`, `crt.PhoneInput`,
`crt.Checkbox`, `crt.ComboBox`, `crt.DateTimePicker`, `crt.ImageInput`, `crt.RichTextEditor`,
`crt.Slider`, `crt.Toggle`, `crt.BarcodeScanner`

**Layout and structure**
`crt.Scaffold` (root — always required), `crt.GridContainer`, `crt.FlexContainer`,
`crt.TabPanel`, `crt.TabContainer`, `crt.ExpansionPanel`, `crt.Label`, `crt.Button`,
`crt.FloatingActionButton`

**Data and collections**
`crt.List`, `crt.FileList`, `crt.Feed`, `crt.Gallery`, `crt.Timeline`

**Navigation / filtering**
`crt.FolderTreeActions`, `crt.QuickFilter`, `crt.QuickFilterGroup`, `crt.Sort`

**Profile and special**
`crt.CommunicationOptions`, `crt.EntityStageProgressBar`

**Widgets**
`crt.IndicatorWidget`, `crt.ChartWidget`

---

## 8. clio MCP Tool Behavior

Mobile pages use the same clio MCP workflow as web pages (`create-page`, `get-page`, `update-page`, `sync-pages`). Mobile-specific differences:

| Tool | Mobile-specific behavior |
|---|---|
| `list-page-templates` | `schema-type: "mobile"` (also `"10"`, `"mobilepage"`) filters to mobile templates only |
| `create-page` | Mobile vs web is determined by the chosen template; response includes `schemaType: 10` |
| `update-page` / `sync-pages` | Auto-detect mobile JSON bodies; reject disallowed sections (`handlers`, `validators`, `converters`) with an error |
| `get-page` | Response `schema-type` field returns `"mobile"` for schemaType=10, `"web"` for schemaType=9, `"unknown"` otherwise |

---

## 9. App and Section Creation

Both `create-app` and `create-app-section` control mobile page generation via `--with-mobile-pages` (CLI) / `with-mobile-pages` (MCP):

| `--with-mobile-pages` | Behavior |
|---|---|
| `true` (default since 8.3.2) | Backend creates web pages **and** mobile pages |
| `false` | Web pages only (sends `clientTypeId = 195785B4-F55A-4E72-ACE3-6480B54C8FA5`) |

For `create-app` the toggle applies to the main entity: `false` suppresses `{code}_MobileFormPage` and `{code}_MobileListPage`, leaving the three web pages (`{code}_FormPage`, `{code}_ListPage`, `{code}_Detail`). An explicit `client-type-id` always takes precedence over `with-mobile-pages`.

Since Creatio 8.3.2, creating a new app or section automatically generates a mobile form page and a mobile list page alongside the web pages.

> **Mobile page visibility requires app/section linkage.** A mobile page schema existing in the environment is not enough by itself to make it visible in the mobile application. The page must be created or linked through the supported app/section creation flow.

---

## 10. OOTB Converters

OOTB converters are referenced as inline pipe expressions in `viewConfigDiff` binding values.
Syntax: `"$Attribute | crt.ConverterName"` or `"$Attribute | crt.ConverterName : arg"`.
Converters can be chained: `"$Attr | crt.A : arg | crt.B"`.

Available: `crt.ToObjectProp`, `crt.InvertBooleanValue`, `crt.IsEqual`, `crt.AndBooleanValue`, `crt.IsInArray`, `crt.Concat`, `crt.ToCollectionFilters`.

See `MobilePageGuidanceResource` for per-converter contracts and usage examples.

---

## 11. Requests

Requests are bound to component events in `viewConfigDiff` (e.g. `clicked`, `valueChange`, `updated`).
Binding syntax: `"clicked": { "request": "crt.<Name>", "params": { ... } }`

Request classes are defined in the mobile-app runtime (`ts/src/lib/requests/`) with `@CrtRequest` decorator. 20 requests are available, grouped into: navigation, record operations, data loading, business processes, files, dialogs/lookups, communication options, and mobile-only (native device) capabilities.

See `MobilePageGuidanceResource` for the full request list with per-request param contracts.

---

## 12. Verified Facts for AI Agents

Facts derived from source verification (not prescriptive rules — see `MobilePageGuidanceResource` for agent directives):

- Mobile pages use web templates with `schemaType=10`; web templates have `schemaType=9`.
- All five templates provide `crt.Scaffold` as the root element.
- The mobile designer maps `Boolean` data type to `crt.Toggle`, not `crt.Checkbox`.
- Mobile page templates inject nodes (Scaffold, buttons, containers) that appear in `bundle.json`.
- The mobile component registry is separate from web; not all `crt.*` web components are available.
- A mobile page must be linked through an app section to be visible in the mobile app.

