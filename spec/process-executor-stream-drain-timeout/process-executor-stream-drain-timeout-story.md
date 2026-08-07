# Process executor stream-drain timeout - story

Status: in-progress

## Story

As an MCP client, I need Git-backed curated-knowledge startup to respect its documented deadline even when a descendant retains redirected pipe handles, so the MCP handshake either receives curated knowledge or proceeds through the existing non-fatal fallback on every supported desktop OS.

## Implementation tasks

- Add a fail-before cross-platform integration regression for the descendant-held-pipe condition.
- Apply the linked operation token to redirected stream reads and preserve timeout/cancellation classification plus partial output.
- Preserve CR/LF/CRLF real-time output boundaries and cancel both readers on output/directory resource limits.
- Expose uncertain descendant termination rather than implying portable process-tree ownership.
- Add a real-process MCP startup regression that proves fake Git invocation, inherited-handle retention, and the existing warning fallback.
- Review `mcp-server` docs, MCP surface, and ClioRing consumers.
- Run Common and MCP targeted validation plus the mandatory comprehensive agentic review.

## Definition of done

- AC-01 through AC-04 in the specification pass.
- No new CLIO diagnostics in modified code.
- Docs and MCP review results are recorded.
- Pull request fixes #1018 and is armed for auto-merge after review.
