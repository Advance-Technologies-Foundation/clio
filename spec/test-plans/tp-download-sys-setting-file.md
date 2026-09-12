# Binary system-setting readback validation

- Unit: required filename, Binary type, missing/empty/malformed/oversized values, exact 10 MB boundary,
  exact byte preservation, existing and concurrently created destination, no temporary files left.
- MCP contract: stable name, long-tail profile, local-write annotations, environment-scoped resolution,
  required absolute file-name and metadata-only success/error results.
- Live: `DownloadSysSettingFileE2ETests`, using real `clio-run` over stdio and the CLI on disposable
  Creatio 10.1.585 (.NET 8, PostgreSQL), with ClioGate installed. Binary bytes, real PNG, UTF-8 text/JSON,
  and UTF-16 XML are uploaded as Binary; both download paths must match every input byte under
  caller-chosen `.chosen` extensions. Existing files must survive repeat calls.
- Refusals: missing/relative MCP filename, ordinary Text and missing setting, with no output file.
- Regression: full Unit suite because Program/BindingsModule changed. Command docs and MCP discovery
  completeness guards included. Inspect Ring consumers for any affected contract.
