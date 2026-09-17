# Native DataService batching

Use native BatchQuery with continueIfError=true and generated query IDs. Reuse DataService expressions, exact-ID filters, environment resolution, non-replayable transport and existing error redaction. Construct native query types internally rather than accepting caller-controlled type discriminators.

The endpoint can return a batch-level error without item results; transaction behavior also depends on server configuration. Therefore the client exposes unknown outcomes and never retries a submitted batch. Per-item success describes the platform response, not an independent readback. Callers verify affected records before resubmitting unknown items.
