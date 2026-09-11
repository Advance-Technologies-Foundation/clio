# manage-role

Manage Creatio role administration through native services with explicit targets and readback.

## Usage

```sh
clio manage-role -e <environment> --action list
```

Use --confirm for every mutation. Inspection does not require confirmation. Mutations are not automatically retried: a timeout or failure can follow a partial change, so inspect the exact target before retrying.

## Options

| Option | Meaning |
|---|---|
| --action | list, create, update, delete, ensure-manager, add-member, remove-member, memberships, members, functional-roles, add-functional, remove-functional Required. |
| --id | Role GUID, new for create.  |
| --name | Exact name filter or new name.  |
| --type | 0 organization, 1 division, 2 manager (list only), 3 team, 6 functional.  |
| --parent-id | Parent role GUID; required for create/ensure-manager.  |
| --user-id | User GUID for memberships.  |
| --functional-id | Functional role GUID to associate.  |
| --effective | Read effective memberships.  |
| --offset | Read offset.  |
| --limit | Page size from 1 to 200.  Default: 100. |
| --confirm | Apply a mutation without an interactive prompt. |
| -e, --environment | Registered Creatio environment. Recommended authentication path. |
| -u, --uri; -l, --login; -p, --password | Inherited connection overrides authenticate the caller. They do not describe the account being managed. |

## Behavior

Create accepts types 0 (organization), 1 (division), 3 (team), or 6 (functional), a name, new id and existing parent-id. The parent must have a compatible type and connection type. A functional role belongs under another functional role or the built-in All employees/All external users anchor; arbitrary organizational parents hide it from the native functional tree. Update accepts a name or parent-id; cycles and moving a manager role are rejected. Delete requires an empty leaf role and refuses root and System administrators roles.

ensure-manager takes parent-id and returns its unique manager child. Creatio automatically creates a manager child for an organization/division. More than one manager child is an error. Managers are a separate role (type 2), not a contact manager field or an arbitrary user flag. Assign a user with add-member using the returned manager ID. Creating missing external manager roles is currently unsupported.

memberships requires user-id; --effective reads the actualized effective memberships instead of direct SysUserInRole rows. Removing a direct membership may leave inherited access. add-functional/remove-functional associate an organizational or manager role (--id) with a functional role (--functional-id).

remove-functional requires ClioGate 2.0.0.50, CanManageSolution and CanManageAdministration. Its server endpoint preserves entity events, actualizes membership, and schedules license redistribution when native feature/settings enable it. Scheduling is not proof that all licenses have changed.

## MCP

Use get-tool-contract for inspect-role (read-only) or manage-role (destructive), then dispatch through clio-run or clio-run-destructive as indicated by the contract. Both accept an args object containing environment-name and the kebab-case options above, except confirm and caller credential overrides. Read get-guidance name=administration first.

Success is exit-code 0 with one Info JSON message. Failure has a nonzero exit code and an Error message. Raw server errors are omitted because administration responses can echo sensitive input.
