# update-entity-schema

Apply batch column operations to a remote Creatio entity schema.


## Usage

```bash
clio update-entity-schema [options]
```

## Description

Apply batch column operations to a remote Creatio entity schema.

After saving the batch the command publishes the configuration and requests an
OData entities rebuild, so changed columns become visible to lookup pickers and
reachable over OData (`/0/odata/<Entity>`) without a manual compile. The rebuild
runs in the background — OData access appears within a few minutes, not
immediately. A 404 (or "The request is invalid") from OData right after the
change is the expected async gap; wait and retry rather than running a full
compile. The whole batch publishes once.

## Examples

```bash
clio update-entity-schema -e dev
```

## Options

```bash
--timeout <NUMBER>
Request timeout in milliseconds. Default: 100000.
--package <VALUE>
Target package name. Required.
--schema-name <VALUE>
Entity schema name. Required.
--operation <VALUE>
Structured operation JSON. Repeat the option for multiple values. Required.
Operation `type` accepts the same values as modify-entity-schema-column, including
Binary, Image, ImageLookup (alias ImageLink), File, SecureText, and Email.
For image/photo fields bound to the `crt.ImageInput` component, add an `ImageLookup`
("Image link") column instead of the binary `Image` type; `ImageLookup` references the
`SysImage` schema automatically (no reference-schema-name).
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
-c, --dev <VALUE>
Developer mode state for environment
--WorkspacePathes <VALUE>
Workspace path
-s, --Safe <VALUE>
Safe action in this environment
--clientId <VALUE>
OAuth client id
--clientSecret <VALUE>
OAuth client secret
--authAppUri <VALUE>
OAuth app URI
--silent
Use default behavior without user interaction
--restart-environment
Restart environment after execute command
--db-server-uri <VALUE>
Db server uri
--db-user <VALUE>
Database user
--db-password <VALUE>
Database password
--backup-file <VALUE>
Full path to backup file
--db-working-folder <VALUE>
Folder visible to db server
--db-name <VALUE>
Desired database name
--force
Force restore
--callback-process <VALUE>
Callback process name
--ep <VALUE>
Path to the application root folder
```

## Requirements

cliogate must be installed on the target Creatio environment.

## Notes

- A `modify` operation on an **inherited** column may override only its caption/description (`title-localizations`/`description-localizations`); changing its name, type, or flags is rejected and stops the batch on that operation.
- `--operation` payloads can include structured `default-value-config`.
- `--operation` payloads can include `usage-type` (`General`, `Advanced`, or `None`; any column type); on `modify` the stored value is left unchanged when omitted.
- For `SystemValue`, clio resolves Guid/alias/caption to canonical Guid before save.
- For `Settings`, clio resolves code/name/id to canonical setting code before save.
- For `Sequence` (text columns only), the static prefix comes from `sequence-prefix` (e.g. `LN-`) or from a `value` mask whose single `{0}` placeholder is at the end (e.g. `LN-{0}` produces `LN-00001`); setting both is rejected. Masks with static text after `{0}` (a suffix) are not supported and fail with a validation error instead of being silently dropped.
- Post-save verification evaluates the final ordered batch state. A later operation may intentionally re-add a column name removed earlier in the same batch.
- `--caption-culture <VALUE>` overrides the culture for written column captions/descriptions (e.g. `en-US`, `uk-UA`) across the whole batch. Precedence: override > the connected user's profile culture (see `get-user-culture`) > `en-US`. When omitted, clio resolves the profile culture and falls back to `en-US` if it cannot be resolved.
- Each `title-localizations` / `description-localizations` value must be written in the language of its culture key. The `en-US` value must be English; a value in a script that does not match a Latin-script culture key (e.g. Cyrillic under `en-US`) is rejected — put localized text under its own culture key such as `uk-UA`.

## See also

- `modify-entity-schema-column`
- `get-entity-schema-properties`
- `get-user-culture`

- [Clio Command Reference](../../Commands.md#update-entity-schema)
