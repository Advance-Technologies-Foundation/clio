# `crt.RichTextLinkComponent` — Internal Rich Text Editor Sub-Component

> Audience: code agent encountering `crt.RichTextLinkComponent` in the codebase.
>
> **This component is NOT inserted via `viewConfigDiff`.** It is an internal tooltip panel managed
> programmatically by the CKEditor integration inside a `crt.RichTextEditor` view element. It opens
> as an inline tooltip when the user selects text and activates the link command in the rich text
> editor toolbar.

## Why this component exists

`CrtRichTextLinkComponent` implements `ITooltipComponent<RichTextLinkTooltipConfig>`. The rich text
editor plugin creates an instance of it through the platform tooltip/overlay infrastructure and passes
it a `tooltipConfig` that contains the active `CKEDITOR.editor` reference, plus three callbacks:
`onClose`, `onSave`, and `onUnlink`.

The component renders a form with two fields — a URL input and a display-text input — validates the
URL with `CustomValidators.linkValidator()`, and either inserts or updates a hyperlink in the editor
when the user submits.

## Schema usage

`crt.RichTextLinkComponent` has no `@CrtInput`/`@CrtOutput` properties and registers zero schema-level
inputs in `ComponentRegistry.json`. It cannot be placed in a page schema. **Do not add it to
`viewConfigDiff`.**

To place a rich text editor on a page, use `crt.RichTextEditor` instead.

## Internal wiring summary

| What | Where |
|---|---|
| Component type in registry | `crt.RichTextLinkComponent` — empty inputs/outputs |
| Angular selector | `crt-rich-text-link` |
| Created by | CKEditor link plugin inside the rich text editor |
| Receives config via | `set tooltipConfig(config: RichTextLinkTooltipConfig)` — not a schema input |
| Editor reference | `config.value.editor` (CKEDITOR instance) |
| Insert/update link | `CKEDITOR.plugins.creatiolink.insertOrUpdateLink(data, editor)` |
