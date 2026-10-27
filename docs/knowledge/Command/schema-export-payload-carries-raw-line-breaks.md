---
description: the platform schema exporter returns MetaData as a JSON string with raw CR/LF inside, which RFC 8259 forbids; export-schema re-encodes schema-data.json, and every reader of a payload must stay on Newtonsoft
applies-to:
  - clio/Command/SchemaTransfer/SchemaBundleStore.cs
ticket: ENG-104664
date: 2026-10-27
---

**What is true** — the platform schema exporter (reached through cliogate `ExportSchema`) returns a payload
whose `MetaData` member is a JSON string with raw carriage-return and line-feed characters inside it.
`SchemaBundleStore.Write` copies the payload token by token through Newtonsoft before it writes
`schema-data.json`, so the file is valid JSON with `\r\n` escapes. The copy uses `DateParseHandling.None` and
`FloatParseHandling.Decimal`, so date strings and fraction digits reach the file as the platform wrote them.
The result is checked with `System.Text.Json`; a payload that fails the copy or the check is written as received,
with a warning. The platform returns such a payload for a schema that was imported and not yet compiled: its
`MetaData` is then in a text format with a backslash before a raw line break, which even Newtonsoft refuses.
The projections (`metadata.json` and the others) and `Read` still parse with Newtonsoft, not
`System.Text.Json`.

**Why it is this way** — strict parsers (`System.Text.Json`, Python `json.load`, `jq`) refuse a raw control
character inside a string, so a verbatim file could not be read or edited by a script (SR-01559669,
clio#1855). Newtonsoft accepts the raw form. The projections parse the payload as the platform returned it,
and `Read` meets bundles that clio wrote before ENG-104664, which still have raw line breaks.

**What breaks if you ignore it** — moving `ParsePayload` or `Read` to `System.Text.Json` makes every real
payload and every old bundle fail to parse: the projections are skipped with only a warning, and import of an
old bundle fails. Writing the payload verbatim again brings back the invalid `schema-data.json`. Re-encoding
through `JObject.Parse` with default settings rewrites ISO date strings and rounds fractions through
`double`, so import sends the platform a different value than it exported.
