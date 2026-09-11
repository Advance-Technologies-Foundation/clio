# Runtime verification for user and role management

Targets: exclusive issue-968 lab and the user-authorized licensed WuestenrotPermissionProbe local lab; both Creatio 10.1.585.0, .NET 8, PostgreSQL.
The compiled Clio CLI and external MCP server are built from this issue worktree.
ClioGate 2.0.0.52 was built for net472 and netstandard2.0, packaged, installed and exercised.

| Area | Evidence | Status |
| --- | --- | --- |
| Package gates | CLI/MCP reject 2.0.0.48 and 2.0.0.49; accept 2.0.0.50; native read actions avoid inventory calls | Unit verified |
| Priority gates | CLI/MCP reject 2.0.0.50/51 before reorder; 2.0.0.52 and verified Creatio version required | Unit verified |
| Password references | CLI and live MCP reject unrelated host variables; dedicated uppercase CLIO_ADMIN_PASSWORD_ references succeed without echo | Unit + external MCP verified |
| Password prerequisite | CLI/MCP reject 8.1.5, 10.1.584 and unversioned development builds before service execution | Unit verified |
| Discovery | Eight tool names/classifications, missing environment envelopes, four inspection mutation refusals | External MCP verified |
| Accounts | Create/contact readback, deactivate/unlock remains inactive/reactivate, password environment references, delete | External MCP verified |
| Authentication | Old password rejected/new password accepted; actual repeated-login lockout followed by unlock and fresh login | Manual native runtime verified |
| Roles/managers | Division and functional create/update/delete; auto manager reuse; assignment and direct/effective memberships | External MCP verified |
| Native UI | Reopened organizational role shows the persisted probe in Users and Managers; nested functional role appears after native tree expansion | Browser verified |
| Manager semantics | Organizational functional association becomes effective for manager; guarded removal removes it | External MCP verified |
| Role-centric members | Direct manager member list; effective user-kind-filtered member query | External MCP verified |
| External lifecycle | ConnectionType=1 account, division, automatic manager, memberships, functional associations, delegation and IP rules | External MCP verified |
| Missing managers | Internal manager recovery works; native external recovery fails with a required Name error and the tool refuses that unverified path | Compiled CLI/native probe verified |
| IP rules | Create/read/update/delete through MCP; actual login allowed with enforcement off, denied with UseRestrictedIP=true, restored after cleanup | Runtime verified |
| Delegation | Grantor role to receiving user adds effective role; revoke removes it | External MCP verified |
| Operation permissions | Grant permits native administration call; deny blocks it; revoke removes fixture grant | Compiled CLI + fresh-session runtime verified |
| Operation priority | Allow-first permits native read; deny-first blocks it after backend/client cache invalidation, including explicit logout/login | Compiled CLI + external MCP verified |
| Cache bridge authorization | Missing CanManageSolution or CanChangeAdminOperationGrantee is denied; preflight failure cannot reorder | Native runtime + unit verified |
| Bridge authorization | Both methods reject missing CanManageSolution and CanManageAdministration; explicit scheduling rejects missing CanManageLicUsers; denied calls preserve association | Native runtime + server log verified |
| License inspection | Empty available-package array handled correctly | External MCP verified |
| Licensed assignment | Five seats assigned, sixth user rejected; direct removal returns all seats; role assignment/removal completes asynchronously; manual source preserved by default and removed with include-manual=true | External MCP verified |
| Scheduling conditional permission | With UseRoleBasedLicenseDistribution=1 and RedistributeLicensesOnRoleChanges=true, missing CanManageLicUsers denies removal and preserves association | Native runtime verified; original setting restored |
| Guidance | Canonical administration article and existing rights/routing links; explicit compatible tool requirements | Producer suite 160 passed |
| Ring | 157 Release tests and Windows x64 NativeAOT publish | Passed; no trim/AOT warnings |

Native denial responses are generic HTTP400. Permission identity was verified from new server error
log entries correlated with the request; HTTP status alone was not treated as proof. Temporary
operation grants and functional associations were removed. The IP restriction setting was restored
to its original false value and the probe authenticated afterward.

The first full Clio unit run found three expected discovery/guidance baseline omissions (12,309
passed). Baselines were updated only after read-only classification review and regeneration from
the knowledge catalog. After rebasing onto current master, the full unit run passed 12,373 tests with 25 skipped and no failures.
The test process must leave CLIO_NO_UPDATE_CHECK unset because an existing updater test expects its
mock updater to run; setting it caused one unrelated refusal before the clean rerun. Live CLI/MCP
processes retain the update-disable setting. All 14 external MCP cases passed, including both internal
and external lifecycles, direct-member removal, effective role-centric members, IP updates and the priority bridge. Knowledge producer
tests passed 160 cases. After the final Claude corrections, the command/MCP module run passed 9,520 tests with 15 skipped; all 14 expanded live MCP cases passed again.

No credentials or password values belong in this record. The initial issue-968 lab has no usable
license packages. The user supplied WuestenrotPermissionProbe for licensed verification; its unused
five-seat add-on was exercised through AdministrationLicenseE2ETests using six disposable users and
contacts and one functional role. All fixtures were removed, and independent readback confirmed all
five seats available and zero used. Native scheduling retained its 60-second delay; the test waited
for real assignment changes instead of treating scheduling receipts as completion. ClioGate 2.0.0.52
was installed on this target. No merge or release claim is made by this lab record.

After merging current master, the complete unit suite passed 12,447 tests with 25 skipped.
The final seven-perspective review found one lifecycle test cleanup timeout issue; both live fixtures
now use independent three-minute cleanup budgets. No blocking review findings remain.
