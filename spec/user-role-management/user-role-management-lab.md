# Source and lab evidence

Issue968 uses kirillkrylov/issue-968 in an isolated Clio worktree based on c470dc580.
Related issues1466-1469 divide user lifecycle, role/managers, licensing and access. Knowledge issue152
owns the coordinated guidance change in its own worktree.

Architecture attempt1 (01a07083-ae2a-7f90-a0d8-59d5ce9f5ede) supplied source and dashboard-rights
pointers, not a verified provisioning implementation. Core/trunk HEAD was
 e0d0f98b80c8fd26e305804c7cb3242b76baf072; generated configuration files are not proven to correspond
to that commit, so deployed10.1.585 source and runtime checks are the authoritative local boundary.

Native contracts confirmed:
- SaveRole writes VwSysAdminUnit from encoded jsonObject and returns encoded success/roleId.
- Organizational insertion auto-creates a manager child; SaveChiefsRole blindly creates another.
  ensure-manager must resolve existing children first. Duplicate children require explicit repair.
- AddUserRoles retains the System administrators additional-authentication guard omitted by
  AddUsersInRole. UI connection-type restrictions must also be preserved on additions.
- UpdateOrCreateUser saves the account before optional creation-time membership work. The deployed
  build logs changed column names; older generated code logs jsonObject including the password.
- UnblockUser resets password and MFA lockout; activation is separate.
- SysAdminUnitInRole is computed membership. Its role field is literally SysAdminUnitRoleId.
- Manager inheritance comes from OrgStructureUser.CalculateManagerRoles, including subordinate
  roles/functional roles and, depending on UserRolesPermissionsInheritanceByManager, their users.
- SysAdminUnitIPRange is independent of global enforcement; UseRestrictedIP was false in this lab.
  IPVersion is virtual UI state. IPv4 comparison is per octet; unsafe representations are rejected.
- SysFuncRoleInOrgRole DataService deletion is restricted. Generic GridUtilities deletion lacks
  the administration check and native UI scheduling behavior; the narrow gateway restores both.
- RightsService owns operation grants, priority and cache effects.
- License role associations and actual user license assignments are distinct. Scheduling native
  ScheduleLicensesRedistribution is asynchronous and cannot prove assignment completion.

Claude consultations: rev_245124dde0f84223 (native identities), rev_6143286a506d4c07 (licenses/access),
rev_d1ab164173414ab0 (functional removal/scheduling), rev_5b34b7a9056f4306 (bridge implementation).
Accepted bridge findings resulted in partial-completion receipts, a conditional licensing permission
check, and preserving scheduling outcomes in the client. Seven independent reviewer perspectives
subsequently checked the implementation. Password logging prerequisites, role-centric member reads,
connection-type validation, explicit operation priority and contract descriptions were addressed.

The additional Claude priority review rev_d0c1c1b16a934319 was quota-limited and produced no verdict.
The security reviewer and runtime decompilation identified the deployed core's public all-user
administrated-operations cache invalidation. ClioGate 2.0.0.52 passed the actual allow/deny priority
probe. A final comprehensive three-perspective review found no high-severity defects; its permission
preflight and safe local-diagnostic recommendations were implemented and tested.

See user-role-management-qa.md for verified results and remaining gaps. No final licensed assignment,
release, merge or complete-delivery claim has been made.

The deployed Terrasoft.Core.dll used for the priority investigation has SHA-256
53444D1A6A0FC6832B65D5BA8E77C2DAF4063DA1CBB933740D33937C0959B1A5.
