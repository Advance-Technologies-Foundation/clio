# Native administration services with explicit agent contracts

Status: accepted for implementation; final validation is pending.

The implementation decision and tradeoffs are owned by
[the feature ADR](../user-role-management/user-role-management-adr.md).

Runtime evidence adds one required bridge: ClioGate checks both CanManageSolution and
CanManageAdministration before deleting a functional-role association. DataService forbids the
mutation and the generic UI deletion service does not enforce the administration operation.
Entity deletion retains native events. Effective roles are actualized before applicable native
license redistribution is scheduled. Explicit license scheduling also requires CanManageLicUsers.

Password mutations execute in the MCP host because worker environments strip inherited secrets.
Read-only inspection has separate tool names that reject mutation actions.
