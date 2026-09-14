# Binary system-setting file readback

Status: review
Issue: #873

Implement the CLI and long-tail MCP adapter described in
`spec/adr/adr-download-sys-setting-file.md`.

Acceptance: filename required; exact bytes without format inference; structured path/count;
existing destinations preserved; missing/non-Binary/invalid values fail without an output file;
environment-scoped resolution; unit and real MCP tests; binary/image/text round trips on an
exclusive disposable Creatio instance; docs and sys-settings guidance aligned.
