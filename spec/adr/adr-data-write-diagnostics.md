# Additive diagnostic context

Use one data-only diagnostic record attached as an optional property to existing responses. Existing success/error/count fields retain their meanings. A server-reported failure does not prove absence of side effects: event listeners may fail after a write. Local validation before submission establishes not-attempted; a submitted write without a successful acknowledgement remains uncertain.

The transport exposes response bodies rather than HTTP status. Report only the boundary actually observed. Keep native error details bounded and sanitized using existing helpers. Field names remain in supplied platform validation text; do not synthesize a field identifier by parsing prose.
