# Binary system-setting readback

- Problem: Binary system settings have an upload path but no file-based MCP readback.
- Outcome: CLI and `clio-run` save exact decoded bytes to a required caller-selected filename.
- Contract: no format inference, no overwrites, explicit failures, metadata-only result; current-user effective value through ClioGate.
- Scope: Binary values only; preserve JSON/XML encodings as bytes. Upload-security policy is unchanged.
- Proof: unit edge cases and real CLI/MCP round trips for binary, PNG, UTF-8 text/JSON, UTF-16 XML on a disposable Creatio instance.
