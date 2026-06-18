# How to Add a Web Chat Preview (`crt.WebChatPreview`) to a Freedom UI Page

> Audience: code agent inserting `crt.WebChatPreview` into a Creatio Freedom UI page schema.
> `crt.WebChatPreview` is a **display widget** that embeds the Creatio web-chat widget inside an iframe
> (`frame-host.html`) and communicates configuration via `postMessage`, rendering a visual preview of
> the chat without live connectivity.

## Metadata

- **Category**: display
- **Container**: no
- **Parent types**: `crt.FlexContainer`, `crt.GridContainer`
- **Typical children**: none

---

## 1. Mental model — the 1-2 places you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | A single `insert` op with `type: "crt.WebChatPreview"` and channel/styling values. **Always present.** |
| 2 | `viewModelConfigDiff` | *(only if binding inputs to page attributes)* |

`crt.WebChatPreview` is **view-only** — it owns no datasource. It resolves the iframe URL from
`WebchatScriptUrlService` at runtime and uses `postMessage` handshake (`crt-webchat-parent-init` /
`crt-webchat-frame-ready`) to initialize the widget.

> **Architecture note:** The iframe runs on the widget-service HTTPS origin (different from the Creatio
> host) to avoid CORS issues. Logo images are converted to base64 data URLs to avoid mixed-content
> restrictions.

### 1.1 Naming convention

```
WebChatPreview_<id>     // view element name
```

---

## 2. Step-by-step recipe

### 2.1 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "WebChatPreview_main",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.WebChatPreview",
    "previewMode": true,
    "channelId": "$ChannelId",
    "primaryColor": "$PrimaryColor",
    "headerTitle": "$HeaderTitle",
    "welcomeMessage": "$WelcomeMessage",
    "logo": "$LogoId",
    "visible": true,
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 1, "rowSpan": 4 }
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.WebChatPreview` are in `ComponentRegistry.json` under `componentType: "crt.WebChatPreview"`.

Inputs: `previewMode`, `channelId`, `embedCode`, `primaryColor`, `headerTitle`, `welcomeMessage`,
`logo`, `additionalProperties`.

---

## 4. Shape of types not in `references.typeDefinitions`

```ts
// additionalProperties — array of name/value pairs forwarded to the iframe config
type AdditionalProperties = Array<{ name: string; value: unknown }>;

// Special handling for specific property names:
// - "privacyPolicyUrl" — value is URL-validated and sanitized (only http/https/file allowed)
// - "privacyNoticeText" — when present, clears "texts.privacyPolicyUrl" in iframe config
```

`logo` accepts either a GUID (resolved via `getImageFullUrlById()`) or a direct URL string.
When the logo is a Creatio-hosted image (relative URL), it is fetched with credentials and converted to a
base64 data URL before being sent to the iframe.

`channelId` / `embedCode` changes trigger the iframe URL resolution; config-only changes
(`primaryColor`, `headerTitle`, `welcomeMessage`, `logo`, `additionalProperties`) send a live
`crt-webchat` `updateConfig` postMessage to the already-loaded iframe.

---

## 5. Copy-paste minimal example

```jsonc
// viewModelConfigDiff — declare channel configuration attributes
{
  "operation": "merge",
  "path": [],
  "values": {
    "attributes": {
      "ChannelId": { "value": "" },
      "PrimaryColor": { "value": "#0070CD" },
      "HeaderTitle": { "value": "Support Chat" },
      "WelcomeMessage": { "value": "Hello! How can we help?" },
      "LogoId": { "value": null }
    }
  }
}

// viewConfigDiff entry
{
  "operation": "insert",
  "name": "WebChatPreview_main",
  "parentName": "PreviewContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.WebChatPreview",
    "previewMode": true,
    "channelId": "$ChannelId",
    "primaryColor": "$PrimaryColor",
    "headerTitle": "$HeaderTitle",
    "welcomeMessage": "$WelcomeMessage",
    "logo": "$LogoId",
    "visible": true,
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 1, "rowSpan": 4 }
  }
}
```

---

## 6. Driving from page state

All 8 inputs are `@CrtInput` and can be bound to page attributes. `channelId` and `embedCode` trigger
iframe URL resolution; the rest send live `updateConfig` messages after the iframe loads.

```jsonc
// viewConfigDiff.values — direct attribute bindings
"channelId": "$ChannelId",
"primaryColor": "$ChatPrimaryColor",
"headerTitle": "$ChatHeaderTitle"
```

---

## 7. Common pitfalls

1. **`channelId` or `embedCode` must be set to resolve the iframe URL.** Without either, `_resolveFrameUrl` is never called and the widget shows a loading state indefinitely.
2. **`previewMode: true`** disables live emoji/files/typing features in the iframe (`PREVIEW_MODE_FEATURES`). Set to `false` only when wiring to a real channel for a live-chat page.
3. **Logo is fetched with credentials when same-origin.** For cross-origin logos the fetch uses `credentials: 'omit'`; CORS must allow the Creatio origin.
4. **URL security validation** — `_normalizeSafeUrl` only accepts `http:`, `https:`, and `file:` protocols; other protocols (e.g. `javascript:`) are silently rejected.
5. **`additionalProperties` de-duplicates by name.** If two entries share the same `name`, only the first is used. Property names must be strings.
6. **`standalone: true` component.** `crt.WebChatPreview` is a standalone Angular component with its own `imports` (`CrtPlaceholderModule`, `TranslateModule`); ensure the host module or module federation setup exposes it correctly.
7. **Load failure** — if `WebchatScriptUrlService` cannot resolve the service base URL, `loadFailed: true` triggers the error placeholder. Check `WebchatScriptUrlService` configuration.

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.WebChatPreview"`, unique `name`.
- [ ] `channelId` or `embedCode` set (or bound to an attribute).
- [ ] `previewMode` set explicitly (`true` for preview, `false` for live).
- [ ] `layoutConfig` provides adequate `rowSpan` for a visible chat widget height.
- [ ] If `logo` is a Creatio image GUID, it is the raw GUID string (not a full URL).
- [ ] `additionalProperties` entries have unique `name` values.
