# Operator bootstrap distribution

Use a versioned manifest bundle exported from the operator repository, shipped in Clio's existing template directory. Keep the export script with the operator; it selects CRDs and the minimal operator resources from its authoritative bootstrap. Do not fork CRD definitions manually in Clio.

The Rancher profile disables the Nexus catalog and in-cluster registry build workflow, omits both node daemonsets, and uses a dashboard ingress. A create-only shared infrastructure resource sets Nexus and MSSQL to External and PG/Redis to Managed; the operator's existing ownership checks preserve external installations. Refuse a conflicting existing Nexus-managed policy rather than changing it behind the user's back.

The bootstrap is host CLI functionality. It does not need a remote MCP tool: Kubernetes credentials and Rancher Desktop live on the host. Ring has no existing consumer of this new command. Use explicit context arguments for every kubectl operation and never switch current-context.

Installation and destructive takeover are separate operations. Retry reuses the existing admin secret and never prints it. A failed rollout leaves diagnosable resources, not an automatic rollback that deletes user data.
