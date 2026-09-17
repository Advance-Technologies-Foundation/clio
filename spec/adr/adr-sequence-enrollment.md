# Native sequence enrollment design

Use one command/service and an environment-resolved MCP adapter. Build an exact Contact-ID filter from the bounded caller selection using existing query helpers. Register the fixed native route in ServiceUrlBuilder; call ExecuteNonReplayablePostRequest with one attempt. Do not accept arbitrary filters or implement eligibility, status transitions, scheduling, capacity or activity creation in clio.

Return native added/failed counts separately from a bounded participant-status snapshot. A transport, malformed-response or schema-contract failure after submission has uncertain completion. Readback is diagnostic, not proof of attribution to this call and not permission to retry. The native engine may change statuses after the snapshot.
