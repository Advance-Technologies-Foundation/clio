using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Resources;

/// <summary>
/// Provides the canonical AI-facing entry point for designing and reviewing Creatio Freedom UI pages
/// so the pages produced through clio MCP look native, are understandable, and meet accessibility expectations.
/// </summary>
/// <remarks>
/// This type ships a thin index <see cref="Guide"/> that orients an agent and routes to three deep leaf guides
/// (<c>ui-page-layout</c>, <c>ui-accessibility</c>, <c>ui-review-checklists</c>). The detailed component map,
/// grid/column math, gap rules, contrast values, color palettes, and audit templates intentionally live in those
/// leaf guides and are not duplicated in the index. The index is reached from the <c>page-modification</c>
/// pre-edit GATE; the leaf names live once, inside this index, so the routing topology stays a clean tree
/// (page-modification -&gt; ui-guidelines -&gt; {ui-page-layout, ui-accessibility, ui-review-checklists}).
/// </remarks>
[McpServerResourceType]
public sealed class UiGuidelinesGuidanceResource {
	private const string DocsScheme = "docs";
	private const string GuidePath = "mcp/guides/ui-guidelines";
	private const string GuideUri = DocsScheme + "://" + GuidePath;
	private const string PageLayoutPath = "mcp/guides/ui-page-layout";
	private const string PageLayoutUri = DocsScheme + "://" + PageLayoutPath;
	private const string AccessibilityPath = "mcp/guides/ui-accessibility";
	private const string AccessibilityUri = DocsScheme + "://" + AccessibilityPath;
	private const string ReviewChecklistsPath = "mcp/guides/ui-review-checklists";
	private const string ReviewChecklistsUri = DocsScheme + "://" + ReviewChecklistsPath;

	/// <summary>
	/// Thin index guidance article accessible by name through <c>get-guidance</c>.
	/// </summary>
	internal static readonly TextResourceContents Guide = new() {
		Uri = GuideUri,
		MimeType = "text/plain",
		Text =
"""
# Creatio UI guidelines

Use this guide to create or review Creatio Freedom UI pages that look native, are understandable for end users, and meet accessibility expectations. This is a thin index — the detailed rules agents get wrong (exact component names, the concept->component map, grid/column math, gap rules, contrast values, audit templates) live in the leaf guides; load the matching one with `get-guidance` before you act.

**Design as a UI/UX expert, not as a data-model dump.** Before composing or editing a page, mentally walk through how a real user will actually fill it in and use it: the order they enter data, what they need to understand at each step, what would confuse or slow them down, what they look at most often. Make deliberate design decisions and justify them with established UX heuristics — clarity, recognition over recall, error prevention, consistency, progressive disclosure, and minimal effort. A page is not "done" when the fields exist; it is done when it is genuinely easy and pleasant to complete and use.

**Judge the rendered page, not the schema.** When reviewing or auditing an existing page, base your findings on the actual RENDERED page (screenshot + live accessibility tree), and walk the fill scenario first — only then reconcile with the schema/metadata. Many defects are visual-only and invisible in the schema or a11y tree: empty/unbalanced islands, group headers that don't render, weak placeholders, spacing/proportion issues. "Looks fine in the schema" is not evidence the page is fine.

## Operating mode

1. Clarify the page goal only when the task cannot proceed without it. Prefer best-effort recommendations over blocking.
2. Identify user roles, main scenarios, data-entry frequency, and whether the page is for create, view, edit, approval, or analytics.
3. Reuse Creatio/Freedom UI patterns before inventing custom controls. Search for analogous Creatio functionality in the target app and align layout, labels, actions, states, and behavior.
4. Produce outputs in one of these forms depending on the user request:
   - page structure proposal;
   - implementation instructions for a Creatio builder/developer agent;
   - UI/UX audit with issues, severity, and fix recommendations;
   - accessibility/WCAG review;
   - copy/label/error text rewrite;
   - acceptance criteria/checklist.
5. Always include an explicit review checklist before finalizing a design or audit.

## Routing — load the matching leaf FIRST (required, not optional)

The rules below are only a reminder. For the task at hand, call `get-guidance` for the matching leaf and follow it before producing a design, an edit, or an audit:

- **Creating, editing, laying out, or reviewing a page** — placing/ordering fields, groups, tabs, profile islands, details/child lists, lookups, buttons, layout/`layoutConfig` -> `get-guidance name=ui-page-layout` (start with its "Concept -> Freedom UI component map").
- **Anything about accessibility, contrast, color, charts, tabs, or custom components** -> `get-guidance name=ui-accessibility`.
- **Producing an audit, review, or acceptance checklist** -> `get-guidance name=ui-review-checklists` (use its severity model and output templates).

When a task spans several of these, read each relevant leaf. Skipping the matching leaf is the main cause of the recurring defects (selection-window lookups, layout gaps, bare fields, non-native details, over-punctuated copy, mismatched label positions).

## Default page design principles

Treat the following as non-negotiable unless the user explicitly overrides them:

- **Keep project customizations visually consistent with the base Creatio product — custom additions must not visually stand out.** Use the default component appearance, typography, colors, spacing, and borders of standard Creatio. A customization that makes the page look different from the base product (even if it seems "nicer" in isolation) is a defect, not an improvement. Deviate only when a real business priority requires it — and then as a deliberate, scoped decision, never as a side effect of another change.
- Prefer no-code/Freedom UI components, predefined typography, predefined colors, templates, business rules, dynamic cases, mini pages, DCM (Dynamic Case Management) stage progress bars, widgets, and existing navigation patterns.
- Make the page understandable without separate instructions where possible. Use structure, labels, placeholders, tooltips, FAQ/help, and validation to guide the user.
- Avoid long, unstructured record pages. Group data into logical tabs, field groups, profile islands, metrics/widgets, related record profiles, progress bar and toggle panels.
- Put required and frequently edited fields early: first tab, visible area, logical order, and with required indication.
- Keep required fields to the real minimum needed to create the record. Mark a field required ONLY if the record is meaningless or cannot be created without it; everything else stays optional (guide it with hints/defaults instead). Over-requiring fields blocks quick creation and frustrates users.
- Use color as a supporting cue, never as the only information channel.
- Validate contrast and accessibility for every custom color, background, chart, tab, image, dialog, and custom component.

## Mandatory implementation rules (most-missed — DO NOT SKIP)

These are the rules agents most often miss. Treat each as a hard requirement and verify it explicitly before finishing any page work. The full mechanics live in `ui-page-layout` / `ui-accessibility`.

- **Few-value lookups MUST be dropdowns, not selection windows.** Any lookup to a small/finite catalog (status, type, category, stage, priority, and similar enum-like sets) must render as an inline dropdown. In Creatio this is controlled at the entity-column level, not by the page: set `simple-lookup: true` on the column (clio `modify`/`add` column op). Standard (non-simple) lookups open the modal selection window — keep that ONLY for large or relationship lookups (Contact, Account, parent record). Never leave a 3-20-value lookup as a selection window.
- **Add tooltips and placeholders to help users fill the form — do not ship bare fields, and do not pad with filler.** Guidance is required where it adds value, not blanket on every field. A field that is not self-explanatory needs a `placeholder` showing the sample value or format, and/or a `tooltip` for longer explanations. Add a `tooltip` to read-only/calculated fields explaining their source. Skip placeholders on self-evident fields and on controls that don't use a free-text hint (lookups/ComboBox, checkboxes). "Enter value" or repeating the label is useless; an unnecessary placeholder is as bad as a missing helpful one. Do NOT append a trailing period to short helper text / a few-word description — over-punctuating every short caption is a defect. Author all tooltip/placeholder text as localizable strings, not inline literals.
- **Check the container's column count BEFORE placing fields.** The grid column count is a property of each container (its `columns` / `columnsCount`). First read the actual column count `N` of the target container, then compute placement relative to `N`: full-width field `colSpan: N` at `column: 1`; a two-column row = `column: 1` + `column: N/2 + 1`, each `colSpan: N/2`. Do not hardcode `12`/`6`/`7`, and do not place a field at a `column`/`colSpan` beyond `N`.
- **No empty layout gaps — number rows sequentially inside each container.** `layoutConfig` coordinates are LOCAL to the immediate parent container and must restart at 1. Within one container number `row` strictly `1,2,3...` with NO skipped index. Never reuse global/absolute row numbers across groups, never give a field an oversized `rowSpan`, and put each field's `parentName` on its real group container.
- **Read the existing page style BEFORE editing, and match it.** Before adding anything, read the current page (`get-page`) and copy the conventions already in use — the ExpansionPanel style, the input `labelPosition`, container spacing/padding/radius, widget sizes, column count — so new components look identical to existing ones. Never drop a differently-styled component next to existing ones (e.g. new inputs with one label position while the existing ones use another).
- **Use a consistent `labelPosition` across a group — but never restyle a component to achieve it.** Use the SAME label position for all inputs within one group/panel; prefer a concrete value (`"above"`/`"left"`). Do NOT change a component's `appearance` just to make a chosen label position render cleanly — keeping the base Creatio look wins.
- **Never restyle components to fix a minor nuance — no scope creep.** Do not change a component's default `appearance`/style across the form to work around a small issue or to satisfy another rule. Keep platform defaults; fix only the specific field if truly needed.
- **ExpansionPanels never sit side by side — they stack vertically, full width.** A `crt.ExpansionPanel` (grouping inputs or wrapping a detail list) always spans the FULL width of its parent. Two-column layout applies to fields inside a panel, not to the panels themselves.
- **Mind spacing between components, and make it fit the content.** Inputs in a grid: NO row spacing (rows sit tight), but keep column spacing. Widgets / charts / metrics: use proportional row AND column spacing so they have room to breathe.

## Page creation workflow

When asked to create or redesign a page, follow this sequence (full detail in `ui-page-layout`):

1. **Scenario map**: state the primary users, primary task, secondary tasks, entry points, and completion signal.
2. **Page pattern**: choose one of: record page with header and tabs, profile/island layout, mini page for add/edit, dashboard/analytics page, wizard/step-by-step page, dialog/modal, or custom component.
3. **Information architecture**: define header, primary display field, required fields, tabs, groups, profile blocks, related records, metrics, and help areas.
4. **Actions**: list top-right buttons, menu actions, inline actions, action states, confirmations, loading/progress behavior, async notifications, and cancellation/undo strategy.
5. **Fields and validation**: define field order, labels, placeholders, filters, default values, required markers, lookup/dropdown choices, read-only explanations, and copy rules.
6. **Accessibility**: verify contrast, keyboard access, accessible names, alt text, semantic roles, and error/status announcements (see `ui-accessibility`).
7. **Acceptance checklist**: provide a concise checklist that an implementer can use before release (see `ui-review-checklists`).

## Review output format

For audits, use this compact format (full templates and the severity model live in `ui-review-checklists`):

```markdown
## Summary
[1-3 sentences]

## Audited pages
- <Page title> (`<SchemaName>`)

## Findings by page (one subsection per page; "No issues found" if clean)
### <Page title> (`<SchemaName>`)
| Severity | Category | Area | Issue | Recommendation |
|---|---|---|---|---|
| High | Accessibility / UX improvement | ... | ... | ... |

## Recommended structure
[Proposed layout/tabs/actions]

## Accessibility checks
[Pass/risk/fix list]

## Acceptance checklist
- [ ] ...
```

Severity guidance:

- **High**: blocks user completion, hides required information, breaks accessibility, or risks destructive/irreversible action without confirmation.
- **Medium**: causes confusion, inconsistency, poor scanability, weak validation, excessive scrolling, or inefficient layout.
- **Low**: polish, copy, spacing, icon, or consistency improvement.
"""
	};

	/// <summary>
	/// Page layout and control leaf guide. Accessible by name through <c>get-guidance</c>.
	/// </summary>
	internal static readonly TextResourceContents PageLayout = new() {
		Uri = PageLayoutUri,
		MimeType = "text/plain",
		Text =
"""
# Page layout and control guidelines

Use these rules for Creatio Freedom UI page creation and UI/UX review.

## Concept -> Freedom UI component map

Conceptual UX terms used in this guide map to concrete runtime component types. When a rule says to use one of these, insert the listed `crt.*` component (always confirm availability/exact name with `get-component-info` against the target environment):

| Concept / UX term | Component type(s) |
|---|---|
| Profile island (side, key stable data) | `SideAreaProfileContainer` (the container); fields laid out via `crt.GridContainer` |
| Field group / collapsible group | `crt.ExpansionPanel` (titled, collapsible) -> fields in `crt.GridContainer`/`crt.FlexContainer` |
| Tab / tab set | `crt.TabPanel` -> `crt.TabContainer` (one per tab) |
| Detail / related (child) list / "Expanded list" | `crt.ExpansionPanel` + `crt.DataGrid` (or `crt.List`) |
| List / grid of records | `crt.DataGrid` (also `crt.List`, `crt.MultiList`, `crt.ListWidget`, `crt.FilterableList`) |
| DCM / status / stage / progress bar | `crt.EntityStageProgressBar` |
| Metric / indicator | `crt.IndicatorWidget` (single value) · `crt.GaugeWidget` (scaled value) · `crt.ChartWidget` (charts) |
| Actions dashboard | `crt.ActionDashboard` |
| Toggle panels | `crt.ToggleContainer` -> `crt.ToggleContainerItem` |
| Layout containers | `crt.GridContainer` (N-column grid) · `crt.FlexContainer` (flex row/column) |
| Lookup field | `crt.ComboBox` (dropdown vs selection window is set on the column via `simple-lookup`, not here) |
| Multi-value lookup | `crt.MultiSelect` |
| Text / number / date / boolean field | `crt.Input` · `crt.NumberInput` · `crt.DateTimePicker` · `crt.Checkbox` |
| Email / phone / web field | `crt.EmailInput` · `crt.PhoneInput` · `crt.WebInput` |
| Button / action | `crt.Button`; grouped actions via `crt.Menu` + `crt.MenuItem` |
| Empty-state placeholder | `crt.Placeholder` |
| Attachments / files | `crt.FileList` (+ `crt.FileInput`) |
| Feed | `crt.Feed` |

## Quick index

Jump to the section you need:

- **Component names** -> "Concept -> Freedom UI component map" (above).
- **Record/form page** -> General product fit · Text, labels, and messages · Page composition · Analytics and metric widgets · Buttons and actions · Grouping and page flow (incl. *Layout coordinates and container nesting*) · Fields.
- **List/section page** -> List (section) page layout.
- **Dialogs** -> Dialogs and modals.
- **Review** -> Common anti-patterns · Default Freedom UI behaviors that silently violate the guidelines.

## General product fit

- New apps and custom pages must align with the overall project solution and Creatio visual language.
- Prefer ready page templates and built-in Freedom UI components. Avoid custom UI unless a standard pattern cannot solve the use case.
- Before creating a new pattern, find an analogous base Creatio implementation and mimic its interaction model, placement, labels, state behavior, and visual weight.
- Design for the actual role and process frequency. Put frequent and critical actions in fast access; move rare actions into menus or later sections.
- Use no-code business rules and dynamic loading to progressively reveal content instead of showing every field at once.

## Text, labels, and messages

- Use **Sentence case** for Creatio labels and headings: only the first word starts with a capital letter unless grammar or a proper noun requires otherwise.
- Avoid Title Case and all-caps headings for fields, groups, details, and dialogs.
- Keep button labels short and result-oriented, for example "Send for approval," not "Click here."
- Use typical wording from existing Creatio interfaces when possible.
- Do not put a period after the final sentence in short UI helper text or button/dialog copy unless the local style guide requires it. The same applies to short column descriptions / a few-word caption — do not append a trailing "." to every short description.
- Error messages for admins and regular users can differ. For regular users, explain what happened and how they can solve it themselves.
- Validate text with a quick usability check: a new user, with no help from the project team, should understand what to do.

## Page composition

- Use ready page templates where possible; they already include standard spacing and alignment.
- **Editing an existing page: match what's already there.** Read the current page (`get-page`) and reuse its established conventions for any new element — ExpansionPanel style, input `labelPosition`, container spacing/padding/radius, widget sizes, column count. New components must look like they were part of the original design, not a different hand. (A frequent defect: new inputs with one label position while the existing inputs use another.)
- Preserve left/right spacing around headers and consistent gaps between containers.
- Put controls in proper containers, not directly onto a grid when alignment will break. For button groups, use flex layout and align heights.
- Prefer a clear "island" structure on complex record pages:
  - record header;
  - profile island for key stable data;
  - metrics/widgets/indicators/charts;
  - related record profiles;
  - progress bar;
  - tabs and toggle panels for secondary information.
- Avoid pages that require long scrolling. Split information into tabs and groups so the user can jump to the needed area in one click.
- Do not waste large side-profile space for only one or two fields. Move such fields into a group or choose a template with a header above tabs.
- **Fill the left/profile column — don't leave it near-empty.** The record-page template ships with a single left island. If the object has many columns, the left side will look empty next to the content area — add a **second left island** below the first, with the **same settings as the existing one** (see *New island / card container — standard settings*), and distribute key stable fields (and/or small metrics) into it so the left column is balanced, not sparse.
- Avoid mixing one-column and two-column fields inside the same group unless it intentionally improves reading flow.

## Analytics and metric widgets

- A page can host analytic widgets — `crt.IndicatorWidget` (single metric/count), `crt.GaugeWidget` (value on a scale), `crt.ChartWidget` (charts), `crt.ListWidget` (embedded list).
- **Place widgets at the top, where they are seen first:** at the top of the **`SideAreaProfileContainer`** (or its template analog), or as the **first elements of the relevant tab**.
- **If there is a lot of analytics, create a dedicated "Analytics" tab** instead of crowding the main/general tab.
- **In `SideAreaProfileContainer` keep only metrics, and keep them small** — widget size **XS or S**. For small metrics, add an **icon** to aid quick visual recognition. Do not put large charts in the side island; place those in the content area or the Analytics tab.
- For the full dashboard/widget layout math, sizing, and styling, call `get-guidance name=dashboards` (and `get-guidance name=indicator-widget` for a metric widget's runtime payload).

## Buttons and actions

- Place page-level buttons in the upper-right page area.
- **Where to put a button (Creatio):**
  - **General / page-level actions** (apply to the whole record — e.g. save, refresh, run a process) go in the page header's **`ActionButtonsContainer`** (top-right).
  - **Context-specific actions** that act on one place — fill a field, run a calculation, generate a value — go **next to the component that shows their result**, not in the header. Put the button beside that field/widget so the cause and effect are visually connected.
- **Wrap buttons in a `crt.FlexContainer`, do not drop them directly on a grid.** Flex adapts to label length and localization (translated captions change width) and keeps button heights/alignment consistent. When a button sits next to an input (e.g. a value field + a "Calculate" button), put **both the input and the button in the same `crt.FlexContainer`** so they align and reflow together.
- Use no more than one **Primary** button in the current context. It is for the main navigation/action or active dialog action.
- Use no more than one **Secondary** button for positive confirmation in the same context.
- Use **Plain** buttons freely for secondary or neutral actions.
- If there are many rare actions, use a button with menu or an actions menu instead of a row of many buttons.
- Buttons in one group must have consistent height and alignment.
- Use an unambiguous icon that matches the action. Add tooltip/accessibility text for icon buttons.
- **Add icons where they aid recognition** — on buttons and menu items, and especially for a set of related actions or several filters: pick distinct, fitting icons (from the Freedom UI icon library) so the UI is scannable and varied, not a row of identical or icon-less items. Keep icon style consistent across the page.
- A button must be visible and active only when it can be used. Hide or disable it based on record state and user rights.
- For state-dependent workflow actions, remove actions that no longer apply, for example hide "Send for approval" once the record is already under approval.

### Button vs menu action vs checkbox

- Use a visible button for frequent actions that materially affect functionality or start a meaningful process.
- Use a menu action for rare actions that affect functionality.
- Use a logical field/checkbox for state data that does not itself execute a process.


## Long-running and destructive actions

- Warn the user before launching an action that can take a long time.
- Show execution status using a loading mask, snack-bar message, countdown, or progress/status area.
- If an operation takes more than 30 seconds or duration is unknown, prefer asynchronous execution and notify the user after completion.
- If an operation can affect system performance, recommend scheduling it outside business hours.
- For long-running external updates, show when data was last updated.
- Any destructive, irreversible, or high-impact action must be cancellable, undoable, or confirmed before execution.

## Typography and visual style

- Freedom UI font is Montserrat. Use predefined typography and colors instead of custom styles.
- Minimize the number of font styles and colors.
- Reuse template styles: Headline 1-4 for headings, Body for regular text, Caption for supporting text.
- Treat color as information coding only with text/icon/status support.
- Before requesting global font, theme, or style overrides, ask why the change is needed and how it affects system perception and consistency.

## Adding and editing data

- Prefer a mini page for creating or editing data when the task is focused, role-specific, or step-limited.
- For larger forms, put required fields on the first tab and visible screen area.
- Use logical field order based on how users fill in data.
- Use validation, input filtering, lookup filtering, default values, and auto-substitution.
- Configure fields used when copying records.
- Use placeholders for examples/format and tooltips for longer instructions (full placeholder/tooltip rules are in the ui-guidelines index — `get-guidance name=ui-guidelines`).
- Do not overload create flows with information needed only later during record processing.

## Grouping and page flow

- Use field groups to divide information. Keep nesting shallow: at most 2 levels.
- Use tabs and groups for large datasets.
- **Group fields by business meaning, and never leave a single lone field as its own group, tab, or profile island.** A container (group / tab / island) should hold a logically coherent *set* of related fields — as a rule of thumb at least 2-3. If a container ends up with one field, either merge it into the related block or add the other fields that belong to that block.
- **Fill out the "main information" block.** The record's primary block (profile island + the first/general tab) must carry the core descriptive attributes a user expects for that record type, not just the `Name` — the identifying who/what/when (owner/responsible, type/status, key dates), grouped together. A near-empty main block (e.g. only `Name` in the island while everything else sits elsewhere) is an anti-pattern: move the key stable fields up into it.
- Put logically related fields next to each other (same group, adjacent rows) so the two-column layout reads as coherent pairs, not random placement.
- If the process has clear steps, show them explicitly or use a wizard to guide the user through it — don't expose the whole data model at once; reveal later fields with business rules as earlier input is filled (progressive disclosure).
- **Implementation — containers for grouping inputs (Creatio):** field groups of inputs are placed either in a **`crt.ExpansionPanel`** (a named, collapsible field group — the standard way to title and fold a set of related fields) or in a **profile island** (the side `SideAreaProfileContainer`, for key stable data). Inside either, lay the fields out with a `crt.GridContainer` (an N-column grid via `layoutConfig`, where N is the container's own column; full-width field `colSpan: N`, half-width `colSpan: N/2`) or a `crt.FlexContainer`. Note `crt.ExpansionPanel` serves double duty: it wraps a **list** to form a detail (related child-records list) *and* wraps **inputs** to form a collapsible field group — pick the children accordingly. Prefer an ExpansionPanel over a bare grid when a group of fields needs a visible title or should be collapsible.
- **Choose Flex vs Grid by whether the content changes at runtime.** A `crt.GridContainer` pins each item to fixed `row`/`column` coordinates, so when an item is **hidden** (a field toggled off by a business rule) or **shrinks** (a `crt.ExpansionPanel` collapsing), its slot stays reserved -> an empty gap. A `crt.FlexContainer` has no fixed slots: siblings pull up/together to fill the freed space, so the layout adapts. Therefore:
  - If any field in a group can be **conditionally hidden/shown** (business rule), place that group's fields in a **flex**, so hiding a field leaves no empty slot.
  - Stack **collapsible panels** (`crt.ExpansionPanel`) and whatever follows them in a **flex**, so the page reflows and lower components pull up when a panel collapses.
  - This is also why buttons go in a flex (they resize to their label) — see *Buttons and actions*.
  - Use a **grid** only for **static, always-present** field layouts where the set of visible items and their coordinates don't change at runtime.

### New island / card container — standard settings

When you add a **new island** (a `crt.GridContainer`/card-style group), apply these standard appearance settings so it matches the native look — do not leave designer defaults:

- **Column spacing:** Large
- **Row spacing:** None
- **Border radius:** Medium
- **Spacing (padding):** Top = Medium, Bottom = Medium, Left = Large, Right = Large
- **Color:** White (the card background that makes the island read as a card).

In the page body these map to container properties (confirm exact keys against an existing island via `get-page`), e.g.:

```jsonc
{
  "type": "crt.GridContainer",
  "color": "white",
  "columnSpacing": "large",
  "rowSpacing": "none",
  "borderRadius": "medium",
  "padding": { "top": "medium", "bottom": "medium", "left": "large", "right": "large" },
  "items": []
}
```

**Plain grid for inputs (NOT an island) — different settings.** When you add a bare `crt.GridContainer` only to lay out inputs *inside* an existing island/panel/tab (no card chrome of its own), use:

- **Column spacing:** Large
- **Row spacing:** None
- **Border radius:** None
- **Spacing (padding):** Top = None, Bottom = None, Left = None, Right = None
- **Color:** Transparent (it must not paint its own background — the parent island/tab shows through).

```jsonc
{
  "type": "crt.GridContainer",
  "color": "transparent",
  "columnSpacing": "large",
  "rowSpacing": "none",
  "borderRadius": "none",
  "padding": { "top": "none", "bottom": "none", "left": "none", "right": "none" },
  "items": []
}
```

Rule of thumb: the **island/card** carries the white background, radius and padding (Medium / L-R Large); an **inner layout grid** is transparent with no radius and no padding (it just arranges fields).

**Spacing must fit what the container holds:**
- **Inputs:** **no row spacing** (rows sit tight) but **keep column spacing** so the two columns don't stick together. (column spacing Large, row spacing None.)
- **Widgets / charts / metrics:** use **proportional row AND column spacing** so they have room to breathe and align evenly — don't cram them with zero gaps. Keep the row and column gaps consistent with each other and with the widgets' size.
- Eyeball the result: gaps between sibling components should look even and intentional, not random.

### Layout coordinates and container nesting (avoiding gaps)

Mechanics behind the layout rules in the ui-guidelines index (`get-guidance name=ui-guidelines`) — read this when a page shows empty gaps or drifting fields:

- **Why a gap appears:** `layoutConfig` is relative to the immediate parent container, and rows/columns restart at `1` in each nested container. A field at `row: 3` while `row: 2` is empty reserves a blank row -> vertical gap; a `column`/`colSpan` beyond the container's column count wraps to a new row and leaves a blank half.
- **Let containers size to content:** use `rows: "auto"` + `gap`; never a fixed `rows` count or an oversized `rowSpan`. One titled group = one container — stack groups with `gap`, not with empty rows.
- **Sanity-check before saving:** in each container, `row` numbers run 1..N with no skips, every `column`/`colSpan` is within that container's column count, and every field's `parentName` is its real group container.

## Fields

- Prefer two-column field layout for standard record fields.
- Use concise, clear field labels. Move explanatory text to placeholder or tooltip.
- Use dropdown lists for lookups with a small number of values.
  - **Implementation (Creatio):** dropdown-vs-selection-window is controlled at the entity-column level, not by the page component. A **simple lookup** column renders as an inline dropdown; a **standard lookup** column renders the modal selection window. Set the column's `IsSimpleLookup` flag (clio `simple-lookup: true` on a `modify`/`add` column operation) for small enum-like catalogs (status, type, category, stage, priority — typically under ~20 rows). Keep standard lookups (selection window) only for large or relationship lookups such as Contact, Account, or a parent record. The page still binds the field as `crt.ComboBox` either way — do not try to fix this with a page-body property.
- Put checkboxes/logical fields after related input fields, usually at the bottom of a group or island.
- Filter lookup fields to relevant values, for example only responsible colleagues, and add a hint about the filter if needed.
- For read-only fields, add a tooltip explaining why the field is read-only and how/when it is filled or calculated.
- Order fields by task flow and requiredness. Required fields must be marked and visible early.
- **Require the real minimum only.** Mark a field required only when the record cannot be created or is meaningless without it (the genuine "minimum to create"). Do not make many fields required — extra mandatory fields block fast creation and push users to enter junk. Keep recommended-but-optional fields optional and guide them with defaults, placeholders, or business rules instead.
- Use a DCM (Dynamic Case Management) stage progress bar — component `crt.EntityStageProgressBar` — for statuses, stages, and ordered process state instead of a loose status combo box. ("DCM" / "progress bar" both refer to this stepped stage indicator.)
- For information easier scanned visually than read as fields, use widgets (see *Analytics and metric widgets*).

## List (section) page layout

The sections above describe the record/form page. A **section (list) page** uses its own container slots — put things in the right one instead of dropping them loose on the page:

- **Additional / custom filters** go in **`LeftFilterContainer`** or **`RightFilterContainer`** (the filter zones beside the standard search/quick filter), not inline in the grid area.
- **Additional list actions / buttons** go in **`ActionButtonsContainer`** (top-right), same as on the record page.
- **Analytics, metrics, and dashboards** go into the **Dashboard component (`crt.Dashboards`)**. If you are not sure how to configure the Dashboard component, place them in **`DashboardsTabContainer`** (the list page's analytics/dashboards tab container) instead — do not scatter widgets into the grid or filter areas.

## Dialogs and modals

- Dialogs should follow the same styling as Creatio mini pages where possible: predictable title placement, button placement, and visual hierarchy.
- Put explanatory text before the fields/buttons that depend on it. Users read from top to bottom.
- Use consistent action labels such as "Save," "Cancel," or domain-specific result verbs.
- Use Plain style for neutral close/cancel actions when appropriate.
- Dialog errors must explain what the user can do next; avoid admin-only technical text for regular users.

## Common anti-patterns to flag in reviews

- Missing section icon.
- Primary display column not configured.
- Required fields placed low on the page or hidden in later tabs.
- Header overloaded with many fields.
- Fields not grouped or grouped inconsistently.
- A single lone field as its own group, tab, or profile island (instead of merging it or adding the related fields of that block).
- Two `crt.ExpansionPanel`s placed side by side / in two columns (panels are full width and stack vertically only).
- Left profile/island too long or too empty.
- Important data does not fit in profile island or header.
- Different languages mixed in one page.
- Abbreviations without tooltips, for example cryptic "F No" or "Op No."
- Units/formats not explained, for example retention term without days/months/years.
- Multiple blue/primary buttons competing for attention.
- Buttons not aligned with fields or each other.
- Buttons placed in a long row instead of a menu.
- Custom controls that look unlike analogous Creatio functionality.
- Checkboxes placed before relevant fields or in visually dominant areas.
- Too many fonts, custom colors, or high-contrast text used as decoration.
- Long pages with tabs far below the fold.
- Empty space under an island while header or central area is overloaded.
- A trailing period appended to short column descriptions / few-word helper text.
- New inputs whose label position differs from the existing inputs on the same page (style not read before editing).
- Dialogs with inconsistent button labels, unclear problem explanation, or text placed below buttons.

## Default Freedom UI behaviors that silently violate the guidelines

Platform defaults that look fine in the designer but break the rules above at runtime. Quick scan list — full guidance is in the topical sections referenced:

- Few-value lookup opens a selection window -> make it a simple lookup (dropdown). [Fields]
- `Date` field shows a time component -> use a date-only column. [Fields / data entry]
- Multi-word captions come out Title Case -> set Sentence case. [Text, labels, and messages]
- Read-only/calculated and other non-obvious fields ship with no tooltip/placeholder -> add guidance. [Fields]
- Ordered status defaults to a combo box -> use a DCM stage progress bar (`crt.EntityStageProgressBar`). [Fields]
- New side island holds only `Name` -> add the key stable fields. [Grouping and page flow]
"""
	};

	/// <summary>
	/// Accessibility and color leaf guide. Accessible by name through <c>get-guidance</c>.
	/// </summary>
	internal static readonly TextResourceContents Accessibility = new() {
		Uri = AccessibilityUri,
		MimeType = "text/plain",
		Text =
"""
# Accessibility and color guidelines

Use this reference when creating or reviewing Creatio Freedom UI pages for WCAG/accessibility.

## Creatio accessibility baseline

- Creatio provides built-in accessibility features for Freedom UI. Since Creatio 8.2.2 Energy, Creatio states compatibility with WCAG 2.0-2.2 AA, Revised Section 508, EN 301 549, and ISO/IEC 40500.
- Accessibility settings are managed in the Accessibility folder in System settings.
- Administrators can configure an accessible desktop color; users can enable it in their profile accessibility settings when configured.


## WCAG principles to apply

### Perceivable

- Provide text alternatives for images, icons, audio, video, charts, and non-text content.
- Avoid images of text. If unavoidable, provide the same text in an accessible format.
- Preserve structure when content reflows or is displayed responsively.
- Do not rely only on color, shape, position, sound, or icon shape to convey meaning.
- Ensure contrast between text/icons/chart lines and background.

### Operable

- All interactive functionality must be available through keyboard.
- Give users enough time; provide options to disable or extend time limits when relevant.
- Avoid flashing content. Nothing should flash more than 3 times per second.
- Navigation and page titles must be clear and consistent.

### Understandable

- Use clear, simple language.
- Avoid unexplained jargon, abbreviations, and project-specific codes.
- Ensure pages and dialogs behave predictably.
- Provide labels, instructions, validation, and error messages that help users avoid and correct mistakes.

### Robust

- Prefer native HTML semantics and built-in Creatio components.
- For custom components, update programmatic names, roles, values, and states.
- **Check every component for its accessibility parameters and that they are filled.** For each component on the page, verify it exposes the accessibility properties it should (accessible name / `aria-label`, label/caption, tooltip, alt text, title) AND that those properties are actually populated — not left empty or at their default. An empty accessibility property is as bad as a missing one; do not assume a component is accessible just because the property exists.

## Freedom UI page criteria (WCAG 2.2 AA — no-code guide)

Beyond the principles above, verify these page-design criteria. Each maps to a WCAG Success Criterion (SC).

### Inputs, forms & validation
- **Meaningful element name (SC 4.1.2):** fill each element's `Title` (top of the properties panel) with a meaningful value — even when the title is visually hidden, assistive tech uses it. An icon-only "add contact" button must be "Add contact", not "Button 1".
- **Error identification, suggestion & prevention (SC 3.3.1 / 3.3.3 / 3.3.4):** clearly identify input errors and offer correction hints (label/tooltip). Mark required fields at the point of entry (or via business rules), not at a later step. For critical/irreversible actions, add a confirmation step or Undo (e.g. a "Confirm order" step before submit).
- **Avoid redundant entry (SC 3.3.7):** never ask the user to re-enter data the system already knows — pre-populate connection lookups (e.g. Account on a Contact created from an account), default addresses, and values carried from process steps.

### Element size & appearance
- **Minimum target size >= 24x24 px (SC 2.5.8):** some Creatio controls (e.g. "S"-size buttons) are smaller than 24 px — leave gap/spacing around them (container gap >= 8 px) so the effective target area is adequate; always space independent buttons apart.
- **Consistent identification (SC 3.2.4):** the same function uses the same icon, label, tooltip, and position across all pages (always "Save", never "Submit"/"Update" for the same action; "Customer Name" labeled the same everywhere).

### Page structure
- **Page title (SC 2.4.2):** keep the `PageTitle` label on every page — it drives the visible title and the browser-tab title, and on record pages auto-fills with the record's primary display value. Edit/move/restyle it, but never delete it.
- **Heading hierarchy (SC 1.3.1):** use Label heading levels H1 -> H2 -> H3 top-to-bottom; exactly one H1 per page/modal; add lower levels only when the structure genuinely needs them.
- **Bypass blocks / skip links (SC 2.4.1):** the Freedom UI shell already provides bypass mechanisms — keep customizations inside the main content area and do not alter `BaseShell`/`MainShell`, so skip-link behavior is preserved.
- **Consistent navigation (SC 3.2.3):** repeated navigation and controls appear and behave the same across pages; if you build a custom layout, apply the same pattern across related pages.
- **Consistent help (SC 3.2.6):** place inline help in the same region across pages (e.g. help icons always immediately right of the field label).

### Localization, links & status
- **Language of page/parts (SC 3.1.1 / 3.1.2):** translate every element (titles, labels, button text) into all enabled languages; don't mix languages on a page unless intended (mark such inputs "Localizable text").
- **Link purpose in context (SC 2.4.4):** link text must convey its destination on its own or with adjacent text — avoid a bare "Click here"; prefer "View pricing table".
- **Status messages (SC 4.1.3):** surface meaningful status messages where needed (e.g. a success message on the Save action); rely on Creatio's built-in notification/validation mechanisms.

## Contrast rules

- Minimum contrast ratio for standard and small text: **4.5:1**.
- Minimum contrast ratio for large text: **3:1**.
- Validate custom color pairs with a contrast checker before applying.
- Pay special attention to widgets, tabs, Area backgrounds, chart values, glassmorphism effects, and desktop wallpapers.
- Prefer dark solid desktop backgrounds. Light wallpapers can reduce contrast for overlay text and widget values.
- Avoid glassmorphism when text or chart values become harder to read.

## Freedom UI color guidance

- Widget Color parameter in Freedom UI Designer and Dashboard Designer shows WCAG-compliant colors; choosing from it is usually safe.
- Pipeline, Sales pipeline, Full pipeline, Doughnut, and Progress bar widgets use preset accessible color sequences and are not user-configurable.
- Tabs and Area backgrounds require manual validation because text/background combinations can fail contrast.

## Recommended multi-series chart order

For bars, stacked bars, lines, and stacked areas with multiple series, use this order to maximize distinction between adjacent series:

1. Blue — `#0058EF`
2. Burnt coral — `#BE1B5A`
3. Dark turquoise — `#08857E`
4. Rusty orange — `#F86700`
5. Light blue — `#009DE3`
6. Purple — `#B87CCF`

If combining bars and lines, validate carefully because similar colors can appear near each other and reduce distinction.

## Tab and title colors

For tabs, configure colors in the Appearance block of the Tabs layout element. Styles include "Fully colored," "Partially colored," and "Plain white." Validate all selected/unselected title colors and tab panel colors.

Recommended tab/title colors include:

- Blue `#0058EF`
- Burnt coral `#BE1B5A`
- Dark turquoise `#08857E`
- Steel blue `#1566B9`
- Vivid purple `#9641A9`
- Cadmium red `#E00022`
- Forest green `#0B6A32`
- Violet `#7848EE`
- Navy blue `#4F43C2`
- Dark blue `#0D2E4E`

Do not assume black, gray, red, green, white, or light tones are valid in every tab configuration. Validate the exact foreground/background pair.

## Area backgrounds

- Avoid Area background colors behind text or chart values unless contrast is verified.
- Light and bright colors often reduce readability. Use them only when they satisfy contrast in the actual component context.

## Progress bars

- Progress bar colors are predefined. Darker colors improve stage readability.
- Recommended progress bar colors: gray `#757575`, blue `#0058EF`, green `#0B8500`, red `#B61303`.
- Avoid light progress bar colors because they reduce stage readability.

## Images, icons, and non-text content

- Informative images must have descriptive alternative text.
- Decorative images must have empty alt text, presentation role, or be implemented as decorative CSS background.
- CSS images that convey information must have an accessible label on the containing element.
- Icon buttons must have visible labels, tooltips, or accessible names.
- Charts and complex diagrams need a text alternative or data table when the information is not otherwise available.
"""
	};

	/// <summary>
	/// Review checklists and output templates leaf guide. Accessible by name through <c>get-guidance</c>.
	/// </summary>
	internal static readonly TextResourceContents ReviewChecklists = new() {
		Uri = ReviewChecklistsUri,
		MimeType = "text/plain",
		Text =
"""
# Review checklists and output templates

Use this reference for audits, acceptance criteria, and final checks.

## Quick audit checklist

### Audit the rendered page, not the schema (do this FIRST)

- [ ] The review is based on the **rendered page** (screenshot + live accessibility tree / DOM), not only the schema, metadata, or `layoutConfig`. Open the actual page and look at it.
- [ ] You walked the user's **fill scenario** on the render before reconciling with the schema — not the reverse.
- Why: many defects are **visual-only** and do NOT appear in the schema or the a11y tree — empty/short or unbalanced left island, group headings that don't actually render, placeholder quality (e.g. junk like "Phone 123"), spacing/proportion problems. "Looks fine in the schema" is not evidence the page is fine.
- [ ] No finding was silently dropped because it looked "intentional" or "temporary" (e.g. a placeholder added for a demo) — flag it explicitly instead of omitting it.

### Think like a user (UX sanity — do this first)

Answer these from the user's perspective before the detailed checks:

- [ ] Does the field/section order match how the user actually fills the page (top-down, required first, dependencies before dependents)?
- [ ] Is the information the user needs most often the most prominent (top, profile island, first tab)?
- [ ] Does anything force recall or extra effort the design could remove (re-typing, hunting for a field, unexplained values)?
- [ ] Are mistakes prevented up front (clear hints, sensible defaults, constrained inputs) rather than only caught after save?

### Scenario and consistency

- [ ] Main user role and task are clear.
- [ ] The page follows an analogous Creatio/Freedom UI pattern where one exists.
- [ ] Custom functionality does not visually conflict with base Creatio styling.
- [ ] The interface can be understood by a new user without project-team explanation.

### Navigation and object setup

- [ ] Section has a unique icon that works in collapsed navigation.
- [ ] Icon style matches Freedom UI: filled, rounded, `#0D2E4E`, SVG where possible.
- [ ] Primary display column exists, is text, required, auto-filled, and ideally unique.
- [ ] Primary display value is useful in page title, lookup, register, and record links.

### Layout and structure

- [ ] Ready templates are reused where possible.
- [ ] Header is not overloaded.
- [ ] Important fields fit in the header/profile area.
- [ ] Long pages are split into tabs, groups, islands, or wizard steps.
- [ ] Field groups have clear names and no unnecessary one-field duplicate-title groups.
- [ ] No container (group / tab / profile island) holds a single lone field; each holds a logically related set (>=2-3 fields).
- [ ] Fields are grouped by business meaning; related fields are adjacent.
- [ ] The main-information block (profile island + general tab) carries the record's core descriptive attributes (who/what/when/status), not just Name.
- [ ] Required/frequently edited fields are on the first tab and visible without long scrolling.
- [ ] Empty space is not created by an oversized side island with too little content.
- [ ] Left/profile column is filled — for objects with many columns a second left island (same settings) is added so the left side isn't near-empty.
- [ ] New islands use the standard settings (white color, column spacing Large, row spacing None, border radius Medium, padding T/B Medium · L/R Large); plain inner input grids use transparent color, column spacing Large, row spacing None, border radius None, padding None — not designer defaults.
- [ ] One-column/two-column mixes do not break reading flow.
- [ ] Container column count was checked first (not assumed 12); `column`/`colSpan` are within that count (two-column = column 1 + column N/2+1, each colSpan N/2).
- [ ] No empty layout gaps: within each container `layoutConfig.row` runs 1..N with no skipped indices, no oversized `rowSpan`, and group containers use `rows: "auto"`.
- [ ] Every field's `parentName` is its intended group container (nesting is correct; coordinates are container-local, not global).
- [ ] ExpansionPanels are full width and stacked vertically — none placed side by side, in two columns, or with a partial `colSpan`.
- [ ] Analytic widgets are at the top (top of profile island or first in the tab; a dedicated Analytics tab if many); the profile island holds only small (XS/S) metrics, with icons, not large charts.
- [ ] Section (list) page: custom filters in `LeftFilterContainer`/`RightFilterContainer`, extra actions in `ActionButtonsContainer`, analytics/dashboards in the Dashboard component (or `DashboardsTabContainer`) — nothing dropped loose on the page.
- [ ] When editing an existing page, new components match the styles already there (panel style, `labelPosition`, spacing/padding/radius, widget size, column count). In particular, new inputs use the SAME label position as the existing inputs.
- [ ] Spacing fits the content: inputs have no row spacing but do have column spacing; widgets/charts/metrics use proportional row + column spacing; gaps between siblings look even.

### Fields and data entry

- [ ] Fields are ordered in the sequence users fill or read them.
- [ ] Standard record fields use two columns where appropriate.
- [ ] Labels are short, clear, and in Sentence case.
- [ ] Abbreviations, units, codes, and formats are explained in tooltip/placeholder/help.
- [ ] Lookup fields are filtered to relevant values.
- [ ] Small enum-like lookups (status, type, category, ~<20 rows) are simple lookups -> render as dropdowns (`simple-lookup: true`); large/related lookups (Contact, Account, parent) use the selection window.
- [ ] Date-only business fields are not rendered with a time picker.
- [ ] Read-only fields explain why/how/when they are filled (tooltip) and show units/scale (placeholder).
- [ ] Non-obvious fields have a placeholder (example/format hint) and/or a tooltip (meaning, units, allowed values); the form is not a wall of bare inputs.
- [ ] Helper text / short descriptions are not over-punctuated — no trailing period appended to every short, few-word description.
- [ ] Tooltip/placeholder text is authored as localizable resource strings, not inline literals.
- [ ] Every input has an explicit `labelPosition` (not `"auto"`), and it is the same for all inputs within a group/panel.
- [ ] Required fields are marked.
- [ ] Only the real minimum is required — fields are mandatory only when the record cannot be created without them; the rest stay optional.
- [ ] Default values, validation, and auto-substitution are configured where helpful.
- [ ] Checkboxes/logical fields are placed after related fields.
- [ ] Status/stage/order uses DCM/progress bar where appropriate.

### Buttons, actions, and dialogs

- [ ] Page-level buttons are in the upper-right area.
- [ ] General/page-level actions are in `ActionButtonsContainer`; context-specific actions (fill/compute a field) sit next to the component that shows the result.
- [ ] Buttons are inside a `crt.FlexContainer` (not dropped on a grid); a button next to an input shares one flex with that input.
- [ ] There is no more than one Primary button per context.
- [ ] Rare actions are moved into a menu.
- [ ] Buttons, menu items, and multiple filters have fitting, distinct icons where they aid recognition (consistent icon style), not a row of identical/icon-less items.
- [ ] Buttons have consistent height and alignment.
- [ ] Buttons are visible/active only when applicable.
- [ ] Destructive or irreversible actions require confirmation, undo, or cancellation.
- [ ] Long-running actions show warning and progress/status.
- [ ] Operations over 30 seconds or unknown duration are asynchronous with notification.
- [ ] Dialogs follow Creatio/mini-page styling and place instructions before controls.
- [ ] Dialog button labels are consistent and result-oriented.

### Copy and content

- [ ] Labels and headings use Sentence case, not Title Case/all caps.
- [ ] Button labels are short and describe the result.
- [ ] Error messages explain what the user can do next.
- [ ] Admin technical details are not exposed to regular users unless necessary.
- [ ] User-facing text is in one language or intentionally localized.

### Typography and visual style

- [ ] Montserrat and predefined Freedom UI typography are used.
- [ ] Font sizes/styles are minimized and based on Headline 1-4, Body, Caption.
- [ ] Colors are minimized and based on predefined palette.
- [ ] Color is not the only indication of status or meaning.
- [ ] Custom global styles/themes have a clear business reason.
- [ ] Components keep the default Creatio appearance — no global restyle (e.g. `crt.Input` switched to `appearance: "outline"`, custom borders/fonts) that makes the form look different from the base product; no restyle done just to satisfy another rule (e.g. label position).

### Accessibility

- [ ] **The `ui-accessibility` guide (`get-guidance name=ui-accessibility`) was actually opened and applied (not skipped, not from memory).** Accessibility is a required dimension of every page/review, not an optional final step — run these checks for every design and audit.
- [ ] Standard/small text contrast is at least 4.5:1.
- [ ] Large text contrast is at least 3:1.
- [ ] Custom tab, Area, chart, glass, and wallpaper combinations are contrast-checked.
- [ ] All interactive elements are reachable and usable by keyboard.
- [ ] Icon-only actions have tooltips/accessibility names.
- [ ] Informative images have alt text; decorative images are ignored by screen readers.
- [ ] Charts/diagrams have text alternative or data table where needed.
- [ ] Each component's accessibility parameters (accessible name/`aria-label`, label/caption, tooltip, alt) are present AND filled in — not empty or left at default.
- [ ] Status changes/no-result messages are announced when relevant; key actions (e.g. Save) give a meaningful status message (SC 4.1.3).
- [ ] Every element has a meaningful `Title` (incl. icon-only / visually-hidden), not "Button 1" (SC 4.1.2).
- [ ] Input errors are identified with correction hints; required fields marked at entry; critical/irreversible actions have confirm or Undo (SC 3.3.1/3.3.3/3.3.4).
- [ ] No redundant entry — known/linked values (lookups, defaults, process-step data) are pre-populated, not re-asked (SC 3.3.7).
- [ ] Interactive targets are >=24x24 px or spaced apart (container gap >=8 px) (SC 2.5.8).
- [ ] The same function is identified consistently across pages — same icon/label/tooltip/position (SC 3.2.4).
- [ ] `PageTitle` is kept; exactly one H1 per page/modal with logical heading order (SC 2.4.2, 1.3.1).
- [ ] Shell (`BaseShell`/`MainShell`) is not altered, so bypass/skip-link behavior is preserved; customizations stay in the content area (SC 2.4.1).
- [ ] Navigation and inline-help placement are consistent across pages (SC 3.2.3, 3.2.6).
- [ ] Link text is descriptive in context — no bare "Click here" (SC 2.4.4).
- [ ] All elements are localized to every enabled language; no unintended language mix (SC 3.1.1/3.1.2).

## Audit output template

```markdown
## Summary
[Brief overall assessment]

## Audited pages
- <Page title> (`<SchemaName>`, <form/list/mini/dialog>)
- <Page title> (`<SchemaName>`, …)

## Findings by page
Report findings separately for each audited page. One subsection per page; if a page has no issues, say "No issues found."

### <Page title> (`<SchemaName>`)
| Severity | Category | Area | Issue | Recommendation |
|---|---|---|---|---|
| High | UX improvement | Required fields | Required fields are below the fold | Move required fields to the first tab and visible area |
| Medium | Accessibility | Contrast | Status text relies on color only | Add an icon/label and meet 4.5:1 contrast |

### <Page title> (`<SchemaName>`)
| Severity | Category | Area | Issue | Recommendation |
|---|---|---|---|---|
| … | … | … | … | … |

**Category** is one of: **Accessibility** (WCAG/contrast/keyboard/alt/announcements) or **UX improvement** (layout, grouping, copy, components, flow).

## Cross-page notes
[Issues that span multiple pages — e.g. inconsistent styles, naming, or spacing across the audited set. Omit if none.]

## Accessibility notes
- Contrast: ...
- Keyboard: ...
- Text alternatives: ...

## Acceptance checklist
- [ ] ...
```

## Page design output template

```markdown
## Page goal
[User role, task, completion signal]

## Recommended Freedom UI pattern
[Record page / mini page / wizard / dashboard / dialog]

## Layout
- Header: ...
- Left/profile island: ...
- Main content: ...
- Tabs/groups: ...
- Widgets/metrics: ...

## Fields
| Group | Field | Behavior |
|---|---|---|
| Basic information | Name | Required, primary display, auto-filled where possible |

## Actions
| Action | Placement | Style | State/confirmation |
|---|---|---|---|

## Copy rules
[Labels, placeholders, tooltip examples]

## Accessibility and validation
[Contrast, alt text, errors, statuses]

## Acceptance checklist
- [ ] ...
```

## Severity model

- **High**: The issue can prevent task completion, make required information inaccessible, create a WCAG failure, trigger an irreversible action without protection, or cause serious misunderstanding of record state.
- **Medium**: The issue slows users, creates ambiguity, adds unnecessary scrolling, causes inconsistent behavior, weakens validation, or makes the page hard to scan.
- **Low**: The issue is mostly visual polish, copy refinement, minor spacing, icon consistency, or optional improvement.

## Common recommendation snippets

- "Move this action into the actions menu because it is rare and competes with the primary action."
- "Use a mini page for creation because the user only needs the required starter fields; move later-process fields to the record page."
- "Split the header into a concise title/status area and move secondary fields into tab groups."
- "Replace this status field with DCM/progress bar to show ordered process state."
- "Add a tooltip explaining the source and editability of this read-only field."
- "Use dropdown instead of lookup because the value set is small."
- "Do not rely on color alone; add label/icon/status text."
- "Validate this custom tab/background pair against 4.5:1 contrast before release."
"""
	};

	/// <summary>
	/// Returns the thin index guidance article that routes to the deep UI design leaf guides.
	/// </summary>
	[McpServerResource(UriTemplate = GuideUri, Name = "ui-guidelines-guidance")]
	[Description("Returns the thin index MCP guidance for designing and reviewing Creatio Freedom UI pages, routing to ui-page-layout (layout & controls), ui-accessibility (WCAG & color), and ui-review-checklists (audit templates).")]
	public ResourceContents GetGuide() => Guide;

	/// <summary>
	/// Returns the page layout and control leaf guide.
	/// </summary>
	[McpServerResource(UriTemplate = PageLayoutUri, Name = "ui-page-layout-guidance")]
	[Description("Returns the Creatio Freedom UI page layout and control guide: the concept-to-component map, grid/column math, container nesting, grouping, fields, buttons, and list-page slots.")]
	public ResourceContents GetPageLayout() => PageLayout;

	/// <summary>
	/// Returns the accessibility and color leaf guide.
	/// </summary>
	[McpServerResource(UriTemplate = AccessibilityUri, Name = "ui-accessibility-guidance")]
	[Description("Returns the Creatio Freedom UI accessibility and color guide: WCAG 2.2 AA criteria, contrast rules, accessible chart/tab/progress-bar palettes, and image/icon text alternatives.")]
	public ResourceContents GetAccessibility() => Accessibility;

	/// <summary>
	/// Returns the review checklists and output templates leaf guide.
	/// </summary>
	[McpServerResource(UriTemplate = ReviewChecklistsUri, Name = "ui-review-checklists-guidance")]
	[Description("Returns the Creatio Freedom UI review checklists, audit/design output templates, and severity model for UI/UX and accessibility audits.")]
	public ResourceContents GetReviewChecklists() => ReviewChecklists;
}
