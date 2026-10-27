---
description: the platform schema exporter inserts Environment.NewLine before every \n escape of its JSON and the importer removes it again; export-schema removes it before writing schema-data.json, so the file is strict JSON
applies-to:
  - clio/Command/SchemaTransfer/SchemaBundleStore.cs
ticket: ENG-104664
date: 2026-10-27
---

**What is true** — the platform exporter (`SchemaImporter.GetSchemaData` in
`TSBpm/Src/Lib/Terrasoft.Core/SchemaImporter.cs`, reached through cliogate `ExportSchema`) writes valid JSON
and then runs `.Replace("\\n", Environment.NewLine + "\\n")` for readability. The importer
(`GetSchemaDataDescriptor` in the same file) runs the reverse replacement before it parses.
`SchemaBundleStore.Write` and `Read` run that reverse replacement too (both CRLF and LF, because the exporter
writes the line break of the server OS), so `schema-data.json` is the exact JSON the exporter produced.

**Why it is this way** — with the inserted line breaks the `MetaData` string carries raw control characters,
so Python `json.load`, `jq` and `System.Text.Json` refuse the file (SR-01559669, clio#1855). The replacement
also hits the tail of an escaped backslash before `n` (`\\n`, a line break in schema source code such as an
entity event process). That leaves a backslash before a raw line break, which even Newtonsoft refuses, so the
projections were skipped for such schemas. Measured on a 10.1.791 stand: the cleaned file imports, and the
re-export is byte-identical to the export.

**What breaks if you ignore it** — re-encoding the payload through a JSON library instead (tried first in
this ticket) escapes each inserted line break as one more `\n`, so the platform receives `MetaData` with
doubled line breaks, and it still fails on `\\n` in source code. Escaping the backslash in `\` + line break or
dropping it makes the platform refuse the import ("Unable to create schema instance ... from metadata"). If the
platform exporter stops inserting the line breaks, the replacement finds nothing and is harmless.
