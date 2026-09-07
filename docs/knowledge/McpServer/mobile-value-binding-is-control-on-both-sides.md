---
description: the mobile value binding's wire name is control, the same as web — the Dart runtime deserializes JSON key 'control' into a field it names 'value', and reading that field name as the wire name is what made the converter withhold every input binding
applies-to:
  - clio/Command/McpServer/Tools/MobilePageConverter/WebToMobileAnalysisService.cs
  - clio/Command/McpServer/Tools/MobilePageConverter/MobilePageConversionGuideModels.cs
ticket: ENG-95827
date: 2026-09-07
---

**What is true** — a mobile input component's value binding travels under the JSON key **`control`**,
exactly as on web. The Creatio mobile (Flutter) runtime declares it:

```dart
@JsonKey(fromJson: bindingAttributeFromJson, name: 'control')
final String? value;
```

`value` is the **Dart field name after deserialization**; `name: 'control'` is the wire key. Every
other channel agrees:

- the mobile registry declares `control` and **no `value` input** for `crt.ComboBox`, `crt.Input`,
  `crt.NumberInput`, `crt.DateTimePicker`, `crt.WebInput` and `crt.Toggle`;
- the web registry marks `control` as the `FormControl` input on **26 of its 28** binding-bearing
  components — the exceptions being `crt.AllowedResults` (`activityResultControl`) and the
  deprecated `crt.DeprecatedInput` (`value`).

So the binding is **not** a type-specific rename between web and mobile. There is nothing to
translate.

**Why it is this way** — the converter used to hold `control`/`value` out of the prebuilt
`values` and report them in a separate `pendingBindings` wire field, documented as *"a mobile
`crt.ComboBox` binds via `value`, while `control` requires `items` or the page crashes"*. That
sentence read the Dart field name as the wire name. `mobileContracts[].allowedProperties` made it
look plausible: it is a union of the registry entry's `Properties` and `Inputs`, so it advertised
`control`, `value` **and** `items` for `crt.ComboBox`, leaving a caller no way to disambiguate.

**What breaks if you ignore it** — emitting `value` instead of `control` produces a component whose
binding the mobile runtime never reads: the field renders, empty, with no error. In the other
direction, withholding the binding cost **31 of 136** inserts on the OOTB `Leads_FormPage` their
value; both recorded conversion runs refused to guess from the response and spent an extra full
`get-page` round trip to recover it, and one of them then wrote `control` — the right answer,
against the tool's own documentation.

Related: **do not prune properties against the mobile registry.** While
`https://academy.creatio.com/api/mcp/latest/MobileComponentRegistry.json` publishes no real
per-component property list, EVERY property is copied from the web component verbatim — the binding
included, and both source spellings (`control` and `value`) copied as-is rather than normalized.
Removing the ones a mobile component does not accept is
[ENG-96589](https://creatio.atlassian.net/browse/ENG-96589), and it is blocked on that registry. The
same rule is already stated for the general case in `BuildMobileValues`, citing the registry's
missing `inputs` (ENG-91859); the binding hold-out was the one exception to it, and it was a
mistake.
