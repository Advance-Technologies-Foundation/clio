# distribute-license

Add or remove users from a Creatio license package.

## Usage

```bash
clio distribute-license --package-id <PackageId> [--add-user <UserId>] [--remove-user <UserId>] [options]
```

## Description

Calls `LicenseManagerProxyService.svc/SaveLicenseData` (the same endpoint used by the Supervisor
"License" section in the Creatio UI) to grant or revoke a license package for one or more users.
At least one of `--add-user` or `--remove-user` is required.

## Aliases

`grant-license`

## Examples

```bash
clio distribute-license --package-id 9c40e123-0a44-4cd2-94de-57341b8c3592 --add-user 7f3b869f-34f3-4f20-ab4d-7480a5fdf647 -e denv
clio distribute-license --package-id 9c40e123-0a44-4cd2-94de-57341b8c3592 --remove-user 7f3b869f-34f3-4f20-ab4d-7480a5fdf647 -e denv
clio grant-license --package-id 9c40e123-0a44-4cd2-94de-57341b8c3592 --add-user id1,id2 -e denv
```

## Options

```bash
--package-id <VALUE>
License package Id (Guid). Find it via the Supervisor > License section in Creatio. Required.
--add-user <VALUE>
User Id (Guid) to add to the license package. Repeat or separate with ',' for multiple users.
--remove-user <VALUE>
User Id (Guid) to remove from the license package. Repeat or separate with ',' for multiple users.
--timeout <NUMBER>
Request timeout in milliseconds. Default: 100000.
```

## Environment Options

```bash
-u, --uri <VALUE>
Application uri
-p, --Password <VALUE>
User password
-l, --Login <VALUE>
User login (administrator permission required)
-i, --IsNetCore
Use NetCore application
-e, --Environment <VALUE>
Environment name
-m, --Maintainer <VALUE>
Maintainer name
```

- [Clio Command Reference](../../Commands.md#distribute-license)
