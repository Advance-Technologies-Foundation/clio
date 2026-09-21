# Sales engagement discovery design

Status: accepted for implementation under clio#1575 and the reviewed parent story clio#1574.

Use one environment-scoped command/service and a thin long-tail MCP adapter. Reuse IRemoteEntitySchemaColumnManager for merged fields and SelectQueryHelper for DataService reads. Never cache lookup GUIDs or select operational audience/contact/mailbox records for discovery. Fixed schema names define the workflow scope; effective metadata and IDs come from the environment.

Bound each collection and the complete response. Report truncation or failed reads explicitly. A successful empty schema-catalog query distinguishes absent sequence schema from a failed metadata read; errors must not be interpreted as absence. Optional sequence inspection reads explicit safe fields and ordered definition steps, not participants or activity content. Schema metadata cannot prove platform service availability or permission for later writes.

Keep the native engine authoritative. Add no orchestrator, retry queue or custom serialization. DataService batch and native enrollment are separate tasks. Task PRs target the parent integration branch; only the final combined Clio PR targets master for full CI. Guidance is delivered separately in clio-knowledge.
