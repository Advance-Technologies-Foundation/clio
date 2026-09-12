# manage-user

Manage Creatio user administration through native services with explicit targets and readback.

## Usage

```sh
clio manage-user -e <environment> --action list
```

Use --confirm for every mutation. Inspection does not require confirmation. Mutations are not automatically retried: a timeout or failure can follow a partial change, so inspect the exact target before retrying.

## Options

| Option | Meaning |
|---|---|
| --action | list, create, update, delete, lock-status, unlock, password Required. |
| --id | Exact account GUID; a new GUID for create.  |
| --user-login | Exact login filter or new login.  |
| --contact-id | Existing contact GUID.  |
| --active | Explicit active state for update.  |
| --external | Create an external user.  |
| --password-env | Name of a populated `CLIO_ADMIN_PASSWORD_<SUFFIX>` process variable; suffix uses uppercase letters, digits or underscores. Never supply the password itself.  |
| --force-change-password | Require a password change at next login.  Default: true. |
| --offset | Read offset.  |
| --limit | Page size from 1 to 200.  Default: 100. |
| --confirm | Apply a mutation without an interactive prompt. |
| -e, --environment | Registered Creatio environment. Recommended authentication path. |
| -u, --uri; -l, --login; -p, --password | Inherited connection overrides authenticate the caller. They do not describe the account being managed. |

## Behavior

User creation requires a new GUID, a unique user-login, an existing contact-id and password-env. Membership is a separate manage-role add-member action. The external flag creates an external account; identity-provider and technical-account provisioning have separate native workflows.

Use --user-login for the account being managed. Inherited --login authenticates the administrator. They are different inputs.

Password changes take only the NAME of an existing process environment variable matching `CLIO_ADMIN_PASSWORD_[A-Z0-9_]+`. This namespace is an operator opt-in: populate it only with temporary account passwords intended for this tooling, in the process launching Clio/MCP, and remove them afterward. Never copy unrelated credentials into this namespace. Never put a new password in command arguments, MCP arguments, logs or guidance. The server must contain Creatio's fix that logs changed column names instead of the UpdateOrCreateUser payload (verified on 10.1.585). LDAP-managed accounts must change their password in their identity provider. The force-change-password default is true. A successful password receipt proves native acceptance and safe account readback, not a fresh authentication test.

Unlock clears password/second-factor lockout and preserves Active. Use update --active true to activate explicitly; native activation can also clear second-factor lockout. Delete uses the platform's protected-user checks.

## MCP

Use get-tool-contract for inspect-user (read-only) or manage-user (destructive), then dispatch through clio-run or clio-run-destructive as indicated by the contract. Both accept an args object containing environment-name and the kebab-case options above, except confirm and caller credential overrides. Read get-guidance name=administration first.

Success is exit-code 0 with one Info JSON message. Failure has a nonzero exit code and an Error message. Raw server errors are omitted because administration responses can echo sensitive input.


Create and password actions require Creatio 10.1.585.0 or newer. Older native implementations can log the password payload. The CLI/MCP version gate rejects older, unknown and unversioned development builds before reading the secret reference.
