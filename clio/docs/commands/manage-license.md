# manage-license

Manage Creatio license administration through native services with explicit targets and readback.

## Usage

```sh
clio manage-license -e <environment> --action user-list --user-id <user-guid>
```

Use --confirm for every mutation. Inspection does not require confirmation. Mutations are not automatically retried: a timeout or failure can follow a partial change, so inspect the exact target before retrying.

## Options

| Option | Meaning |
|---|---|
| --action | user-list, user-assign, user-remove, role-list, role-assign, role-remove, role-redistribute Required. |
| --user-id | Target user GUID.  |
| --role-id | Target role GUID.  |
| --package-id | License package GUID.  |
| --offset | Read offset.  |
| --limit | Read page size from 1 to 200.  Default: 100. |
| --include-manual | Permit redistribution to change manual assignments.  |
| --confirm | Apply a mutation without an interactive prompt. |
| -e, --environment | Registered Creatio environment. Recommended authentication path. |
| -u, --uri; -l, --login; -p, --password | Inherited connection overrides authenticate the caller. They do not describe the account being managed. |

## Behavior

user-list/user-assign/user-remove use user-id; assignment/removal also requires package-id. Personal license changes reject technical accounts; activate the account before assigning a license. The server decides availability and validates capacity. Only the specified package is changed.

role-list/role-assign/role-remove use role-id; mutations also require package-id. These change the role's package association. Their receipt explicitly says redistributionRequired=true and userAssignmentsVerified=false.

Run role-redistribute with role-id to schedule native redistribution, then inspect affected user licenses until the intended state is verified. It requires ClioGate 2.0.0.50, CanManageSolution, CanManageAdministration and CanManageLicUsers, and role-based licensing must be enabled. Manual assignments are preserved unless --include-manual is explicit. Scheduling is delayed by native settings; the tool does not claim eventual completion.

License mutation verification requires a licensed sandbox. The unlicensed issue lab proves the empty availability result only.

## MCP

Use get-tool-contract for inspect-license (read-only) or manage-license (destructive), then dispatch through clio-run or clio-run-destructive as indicated by the contract. Both accept an args object containing environment-name and the kebab-case options above, except confirm and caller credential overrides. Read get-guidance name=administration first.

Success is exit-code 0 with one Info JSON message. Failure has a nonzero exit code and an Error message. Raw server errors are omitted because administration responses can echo sensitive input.
