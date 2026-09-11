# Automatic update defaults

Issue: #1443

Users repeatedly ask to disable automatic tool replacement. Default clio and toolkit updates to disabled and keep knowledge enabled. Preserve all explicit current and legacy preferences.

Use the existing policy initializers and null normalization; remove the two obsolete migrations that re-enable legacy false. Align shipped settings, schema completion and CLI help. Reuse existing scheduling and manual commands. No new state, migration version, service, or protocol is needed.

Validate fresh, missing, empty, null, legacy scalar and explicit object settings through bootstrap and scheduling. Run affected Command/Common tests and the full unit suite because shared settings changed. No Creatio deployment or MCP contract change is involved.
