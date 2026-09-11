# Partial results for named tool contracts

Issue: https://github.com/Advance-Technologies-Foundation/clio/issues/1408

Related issue: https://github.com/Advance-Technologies-Foundation/clio/issues/1406

The named lookup currently exits on its first unknown name. Resolve every distinct,
trimmed name and retain successful contracts in request order. Add an optional
`not-found` array containing each missing `name` and its existing structured `error`
(including suggestions). `success` means at least one named contract resolved.

When every name misses, preserve `success: false`, null `tools`, and the first
top-level `tool-not-found` error for existing clients, while reporting all misses.
Omit `not-found` when there are no misses so successful single-name and catalog
responses keep their existing shape. Blank names remain whole-request validation
errors. No stop-on-error option or new resolution service is needed.

The reported missing OAuth tool already exists but is gated by `deploy-identity`.
Publish the four documented remote workflow commands and their MCP adapters by
removing that attribute: get-identity-service-config, resolve-oauth-system-user,
create-server-to-server-oauth-app, and verify-oauth-app. The user approved this
publication scope. Keep deploy-identity and create-oauth-technical-user gated.
Preserve existing authentication, destructive flags, and secret handling. No
server filesystem or database access is introduced; these commands use HTTP APIs.
The existing external OAuth guide becomes usable without changing its workflow.

ClioRing requests the unnamed catalog and individual named contracts. Preserve
these paths and verify its tests and Windows NativeAOT publish. Unit tests cover
ordering, deduplication, all misses, and invalid names; stdio E2E covers mixed
batches and serialization. The full executable contract documents the new field.
