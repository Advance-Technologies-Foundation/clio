# Package-owned process element registration

Issue: #1599, child of Story #1598. The user approved separate primitives for task
creation, registration and parameter-page creation. This feature implements only
registration of an existing task by schema UId.

Required inputs: explicit workspace path, owning package name, task schema UId and
initial caption. Validate local workspace membership and matching descriptor /
metadata identities before writing files. Produce PostgreSQL and SQL Server native
after-package SQL scripts. Repeated generation preserves matching artifacts and
their UIds; conflicting existing content is rejected without overwrite.

Installation inserts a registration only when the task belongs to the expected
package and no registration exists. Existing registration identities and captions
remain unchanged. Deployment uses existing package installation commands.

Acceptance: CLI/MCP parity, offline E2E generation, negative identity validation,
stable retries, PostgreSQL clean installation/reinstallation and toolbox visibility.
SQL Server runtime validation is coordinated in #1602 using an existing instance;
none may be created or removed under the user's latest instruction.
