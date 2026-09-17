# Generate custom process element registration

Status: review
Issue: #1599; parent #1598.

Implement `register-process-element` with required workspace, package, user-task UId
and caption. Reuse native SQL installation descriptors and existing deployment tools.

- [x] CLI and MCP generate both dialects without an environment connection.
- [x] Ownership, duplicate UId and conflicting-artifact checks precede writes.
- [x] Retry preserves descriptors, scripts and installed registrations.
- [x] Unit, MCP E2E and retained PostgreSQL installation/visibility checks pass.
- [x] Command help, docs and tool descriptions agree.
- [ ] Required review and delivery gates pass.

SQL Server installation evidence remains tracked by dependent #1602.
