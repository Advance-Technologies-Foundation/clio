# Download Binary system settings without interpreting content

Issue: #873. Status: accepted.

Add `download-sys-setting-file --code <code> --file-name <destination> -e <environment>`
and an equivalent long-tail MCP tool reached through `clio-run`. The destination must include
a filename; MCP requires an absolute path. Do not infer MIME types, extensions, or text encodings.

Reuse the existing ClioGate system-setting read, which resolves the authenticated user's effective
value (including its default fallback). Verify the definition is Binary, require a nonempty valid
Base64 response within the existing 10 MB binary limit, decode it, and publish the exact bytes using
a same-directory temporary file and a non-overwriting move. An absent/empty value fails explicitly.
Do not use the legacy manager's failure-to-empty fallback for a file download.

Return saved path and byte count, plus normal execution diagnostics. The MCP tool writes locally,
so ReadOnly=false; it never overwrites, so Destructive=false. No resident tool or protocol changes.
Existing upload security policy is outside scope. JSON/XML uploaded as Binary are opaque bytes;
ordinary Text settings are rejected. Verify binary, real PNG, UTF-8 text/JSON, and UTF-16 XML on a
new disposable local instance, through both CLI and `clio-run`, including mismatched extensions.
