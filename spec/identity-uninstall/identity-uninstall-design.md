# Optional IdentityService attachment

User-approved design, 2026-09-12. Supersedes database-driven attachment discovery
discussed in the earlier scope review. Source: 08558fc856540b69e341a700984cc5b6f6507277.

## Contract

Each registered environment describes its optional installed identity in Clio
appsettings. Use an IdentityService object with EnvironmentPath, IisTarget,
ApplicationPool and Uri strings. Empty values mean no attachment; the settings JSON
schema explicitly documents the object and allows absent/null/empty older values.
Newly serialized environments show the empty fields when no identity is installed.
No database queries, URL-to-IIS discovery or inferred naming establishes attachment.
The saved record is the operator-maintained authority for local cleanup.

Deploy records actual resolved target details independently of OAuth client creation,
including --no-app. Reserve the environment and target path using existing reservations;
validate paths and existing IIS targets before recording or mutating. Persist the
validated target before deployment stages that can leave artifacts, so failed
deployments remain removable. Reject replacing a different nonempty attachment.
Do not record secrets or introduce an independent registry or recovery framework.

Uninstall reads the attachment from the current environment registration. Absent or
all-empty means no identity. Partially populated metadata fails before destructive
work. Revalidate the recorded physical path and IIS target, refuse conflicting targets
and overlap with CRM/other registered environments, and preserve shared pools using
IIisScanner. A complete IIS inventory is a deletion safety check, not attachment
discovery. Detect duplicate attachment path/target across registered environments and
refuse destructive cleanup. Do not parse databases to reconstruct ownership.

Standalone uninstall authenticates and clears only CRM settings still matching the
recorded identity while identity is available; then removes recorded IIS target,
unused pool and files; then clears matching Clio OAuth references and resets the
attachment to empty. Leave technical users, OAuth client rows and database tables.
Never silently switch authentication schemes. If cleanup cannot be authenticated,
fail before deleting identity and retain the attachment for retry.

Combined uninstall prevalidates CRM and identity together before destructive stages.
Identity cleanup completes before CRM database/file deletion; do not make HTTP CRM
configuration updates when CRM itself is being removed. Clear identity attachment only
after successful identity cleanup, while retaining CRM registration until CRM cleanup
finishes. Identity cleanup never drops a database. Existing CRM pipeline drops its
database once. Missing recorded identity files/site are idempotent if surviving targets
remain consistent; retain attachment after partial failure. An old environment without
attachment keeps existing CRM removal behavior, without inferring deletion from OAuth
credentials. If registered sharing is detected, stop the combined run before deleting
CRM's database rather than destroying a dependency of an identity being preserved.

Expose uninstall-identity CLI/MCP under the existing deploy-identity feature key.
Combined removal honors recorded installed components even if deployment is disabled.
Use the existing progress/event contract additively and update reporting/confirmation.
Tests cover empty/absent metadata, settings/schema roundtrip, partial deployment,
--no-app, targeted reference cleanup, topology changes, shared pool/attachment,
path overlap, missing artifacts and partial-failure retry, and no double database drop.
Run required Ring tests/harness/NativeAOT and disposable local IIS validation.

## Remaining implementation decisions

Resolve the actual Clio appsettings schema source and update it; do not edit Ring's
separate settings schema by mistake. Choose the smallest atomic settings update method
that preserves unrelated fields and compares the expected attachment before clearing.
Use existing sys-settings transport; no new database discovery layer.

## Approved review corrections

Claude review rev_5b50c5353bb14bc4 agreed to proceed. Merge copies only nonempty
attachments, preserving both ordinary updates and environment rename. Explicit
compare-and-update handles clearing. Existing IIS bindings must match the recorded
URL; overwrite reuses the recorded port when none is supplied. Standalone clears
secret/client-id before URL, then checkpoints CrmReferencesCleared in the attachment
before deleting files, allowing retry without the removed identity. An explicit
--skip-crm-cleanup recovery option leaves CRM settings untouched and reports a warning;
it never silently switches credentials. Identity cleanup runs after CRM read-config
and before CRM stop-iis. No separate ownership registry or database discovery.
