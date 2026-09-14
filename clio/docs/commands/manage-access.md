# manage-access

Manage Creatio access administration through native services with explicit targets and readback.

## Usage

```sh
clio manage-access -e <environment> --action ip-list --unit-id <user-or-role-guid>
```

Use --confirm for every mutation. Inspection does not require confirmation. Mutations are not automatically retried: a timeout or failure can follow a partial change, so inspect the exact target before retrying.

## Options

| Option | Meaning |
|---|---|
| --action | Operation: ip-list, ip-create, ip-update, ip-delete, delegations, delegate, revoke-delegation, operations, operation-grants, grant-operation, deny-operation, revoke-operation, operation-position. Required. |
| --id | IP rule or operation grant record GUID.  |
| --unit-id | Target user or role GUID; grantee for delegation and operation permissions.  |
| --grantor-id | User or role whose rights are delegated to the target user.  |
| --operation-id | System operation GUID.  |
| --code | Exact operation code filter.  |
| --begin-ip | Beginning canonical IPv4 address.  |
| --end-ip | Ending canonical IPv4 address.  |
| --position | Zero-based operation grant priority.  |
| --offset | Read offset.  |
| --limit | Read page size from 1 to 200.  Default: 100. |
| --confirm | Apply a mutation without an interactive prompt. |
| -e, --environment | Registered Creatio environment. Recommended authentication path. |
| -u, --uri; -l, --login; -p, --password | Inherited connection overrides authenticate the caller. They do not describe the account being managed. |

## Behavior

ip-list/ip-create/ip-update/ip-delete use unit-id. Creates and updates require an explicit rule id, begin-ip and end-ip. Rules attach to the chosen user or role; manager access rules attach to the manager child ID. Every beginning IPv4 octet must be no larger than its corresponding ending octet, matching native enforcement. IPv6 writes are refused because affected native versions fail mixed-family login checks. Deleting the last applicable rule can remove IP restrictions.

Delegation uses grantor-id as the source of rights and unit-id as the receiving USER. Technical users and self-delegation are refused. delegates is not an action: use delegate, revoke-delegation or delegations. Effective membership is refreshed, and the direct pair is verified.

operations optionally filters an exact code. operation-grants uses operation-id. grant-operation/deny-operation/revoke-operation use operation-id and unit-id; they call RightsService so native permission checks, two-factor checks, ordering and cache invalidation run. operation-position uses the GRANT RECORD id and a zero-based position. Deny and removal are different: removal allows lower-priority or inherited grants to determine access.

These are administration access rules. Record-level rights continue to use get-record-rights/set-record-rights.

operation-position requires Creatio 10.1.585.0 and ClioGate 2.0.0.52 or newer. The command uses native priority ordering, then invalidates the native backend and browser rights caches through the guarded bridge. The native ordering endpoint alone leaves cached permission decisions stale on the verified runtime. A failure may occur after the position changed; inspect the grant and retry the intended priority explicitly. No grant value is rewritten to force cache invalidation.

## MCP

Use get-tool-contract for inspect-access (read-only) or manage-access (destructive), then dispatch through clio-run or clio-run-destructive as indicated by the contract. Both accept an args object containing environment-name and the kebab-case options above, except confirm and caller credential overrides. Read get-guidance name=administration first.

Success is exit-code 0 with one Info JSON message. Failure has a nonzero exit code and an Error message. Raw server errors are omitted because administration responses can echo sensitive input.


IP-rule persistence does not enable enforcement. Native login checks require UseRestrictedIP=true or server authentication UseIPRestriction=true. Enable that environment-wide setting only within authorized scope and verify a fresh login from the intended client address. operation-position requires an explicit position; omission never defaults to highest priority.
