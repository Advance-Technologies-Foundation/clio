# `crt.RichTextVideoComponent` — Internal Rich Text Editor Sub-Component

> Audience: code agent encountering `crt.RichTextVideoComponent` in the codebase.
>
> **This component is NOT inserted via `viewConfigDiff`.** It is a `MatDialog` panel opened
> programmatically by the CKEditor video plugin inside a `crt.RichTextEditor` view element. It appears
> when the user activates the video-insertion command in the rich text editor toolbar.

## Why this component exists

`CrtRichTextVideoComponent` is opened by `MatDialog` with a data payload of `{ value: { editor } }`,
where `editor` is the active `CKEDITOR.editor` instance. The component renders a tabbed dialog with
three insertion modes:

- **Paste URL** — embed a video by URL
- **Upload file** — upload a local video file
- **Embed code** — paste raw HTML embed code

After the user confirms, the component calls `editor.insertElement(div)` to inject the video wrapper
into the CKEditor document, then closes the dialog.

## Schema usage

`crt.RichTextVideoComponent` has no `@CrtInput`/`@CrtOutput` properties and registers zero schema-level
inputs in `ComponentRegistry.json`. It cannot be placed in a page schema. **Do not add it to
`viewConfigDiff`.**

To place a rich text editor on a page, use `crt.RichTextEditor` instead.

## Internal wiring summary

| What | Where |
|---|---|
| Component type in registry | `crt.RichTextVideoComponent` — empty inputs/outputs |
| Angular selector | `crt-rich-text-video` |
| Created by | CKEditor video plugin via `MatDialog.open(CrtRichTextVideoComponent, { data: { value: { editor } } })` |
| Tab types | `TabType.Url = 0`, `TabType.Local = 1`, `TabType.Embeded = 2` |
| Insert result | `editor.insertElement(div)` with a `<span class="resizable-video-wrapper">` |
| Unsaved changes guard | `currentTab.hasUnsavedChanges()` triggers a confirmation dialog on close |
