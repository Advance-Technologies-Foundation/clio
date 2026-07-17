# distribute-license

Add or remove users from a Creatio license package.

## Usage

```bash
clio distribute-license --package-id <PackageIdOrName> [--add-user <UserIdOrName>] [--remove-user <UserIdOrName>] [options]
```

## Description

Calls `LicenseManagerProxyService.svc/SaveLicenseData` (the same endpoint used by the Supervisor
"License" section in the Creatio UI) to grant or revoke a license package for one or more users.
At least one of `--add-user` or `--remove-user` is required.

`--package-id`, `--add-user` and `--remove-user` each accept either a Guid or an exact,
case-insensitive display name, resolved via the same `LicenseManagerProxyService.svc` endpoints the
Supervisor "License" UI itself calls to populate its pickers:
- A user name is resolved via `LicenseManagerProxyService.svc/GetUsersList`.
- A license package name is resolved via `LicenseManagerProxyService.svc/GetLicenses`.

Both endpoints are license-exempt — like `SaveLicenseData` itself — so name resolution works even
on a fresh environment with zero distributed licenses (this is how an administrator grants the very
first license through the UI without going through DataService). Resolution fails loudly (no
request is sent) when a name matches zero or more than one record — pass the Guid directly in that
case.

## Aliases

`grant-license`

## Examples

```bash
clio distribute-license --package-id 9c40e123-0a44-4cd2-94de-57341b8c3592 --add-user 7f3b869f-34f3-4f20-ab4d-7480a5fdf647 -e denv
clio distribute-license --package-id 9c40e123-0a44-4cd2-94de-57341b8c3592 --remove-user 7f3b869f-34f3-4f20-ab4d-7480a5fdf647 -e denv
clio grant-license --package-id 9c40e123-0a44-4cd2-94de-57341b8c3592 --add-user id1,id2 -e denv
clio distribute-license --package-id "studio creatio on-site subscription" --add-user Supervisor -e denv
```

## Options

```bash
--package-id <VALUE>
License package Id (Guid) or exact package name as shown in the Supervisor > License section in
Creatio. Required.
--add-user <VALUE>
User Id (Guid) or exact user name to add to the license package. Repeat or separate with ',' for
multiple users.
--remove-user <VALUE>
User Id (Guid) or exact user name to remove from the license package. Repeat or separate with ','
for multiple users.
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
