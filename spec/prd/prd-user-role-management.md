# Agent user and role management

The user needs an agent to administer Creatio users, organizational and functional roles,
manager roles, memberships, licenses and administration access, including password reset and unlock.

The accepted scope and split issues are maintained in
[the feature scope](../user-role-management/user-role-management-scope.md).
Existing dashboard/record-rights guidance must route account and role provisioning to a single
administration owner in clio-knowledge. Local runtime evidence is a delivery requirement.

Do not replace native authorization with arbitrary SQL or infer success from HTTP status.
Secret values must not appear in command arguments, MCP arguments or emitted results.
