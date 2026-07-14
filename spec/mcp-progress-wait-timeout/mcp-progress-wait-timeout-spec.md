# MCP progress wait timeout specification

## Problem

The MCP E2E progress-capture helper returned its latest snapshot when a timeout expired, even when the requested condition was false. Under TeamCity load, the invalid-archive deploy test therefore asserted a stale partial sequence and failed unrelated pull requests with an event-order message.

## Requirements

- Wake progress waiters when a notification is captured instead of polling every 20 milliseconds.
- Re-read the queue once at the timeout boundary before deciding that the condition failed.
- Return only a snapshot that satisfies the caller's condition.
- Throw an explicit timeout containing secret-safe typed-event summaries when the condition remains false.
- Keep the corrupt-archive validation non-destructive; no Creatio instance may be installed or uninstalled.

## Exclusions

- No production MCP event schema, ordering, or tool behavior changes.
- No relaxation of the required terminal `run-completed` assertion.
