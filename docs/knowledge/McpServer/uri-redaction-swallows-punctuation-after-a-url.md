---
description: SensitiveErrorTextRedactor replaces a URI with everything up to the next whitespace, so a ")" or "." pressed against a URL disappears with it and the MCP-channel message arrives malformed
applies-to:
  - clio/Command/McpServer/SensitiveErrorTextRedactor.cs
  - clio/Package/ServiceResponseJsonGuard.cs
ticket: GH-1322
date: 2026-09-05
---

**What is true** — the redactor's URI pattern is `\b[a-zA-Z][a-zA-Z0-9+.\-]*://[^\s"'<>]+`: it runs
from the scheme to the next whitespace character. Any punctuation written immediately after a URL is
part of the match and is replaced along with it. A message built as
`"... (URL: https://host/route). Next sentence"` therefore reaches an MCP client as
`"... (URL: [redacted-uri] Next sentence"` — the closing parenthesis and the full stop are gone, and
the reader sees an opening bracket that never closes. The CLI channel, which does not redact, shows
the same message correctly, so the defect is invisible unless the message is read through MCP.

**Why it is this way** — the pattern is deliberately greedy to the next whitespace: a URL can legally
contain `)`, `.`, `,` and `;`, and a conservative pattern that stopped at them would leak the tail of
a path or query string. Over-redacting punctuation is the accepted price.

**What breaks if you ignore it** — an error message that an agent copies into a transcript comes out
with unbalanced brackets or two sentences fused into one. Write the URL as its own trailing segment
followed by a space (`"... . URL: <url> Next sentence"`) rather than inside brackets or before a full
stop, and the message stays well-formed in both channels.
