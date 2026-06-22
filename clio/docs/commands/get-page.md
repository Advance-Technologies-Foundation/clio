# get-page

## Command Type

    Development commands

## Name

get-page - Read a Freedom UI page as a merged bundle plus raw schema body

**Aliases:** `page-get`

## Description

The get-page command resolves the requested Freedom UI page, loads its full
designer hierarchy, builds the effective merged bundle, and returns a JSON
envelope with nested page metadata, bundle data, and raw.body. Use raw.body
as the editable payload for update-page.

The command resolves the design package for the schema and uses the top of
the parent-schema hierarchy to load the editable version. This ensures that
raw.body is always read from the package where the schema can be modified.

## Conflict-Detection Baseline

The response carries an `editable` block with the editable schema state captured at
fetch time (best-effort — a failed capture never fails get-page):

```jsonc
{
  "editable": {
    "editableSchemaExists": true,
    "editableSchemaUId": "…",
    "checksum": "…",        // SysSchema.Checksum at fetch time
    "modifiedOn": "…"        // informational only
  }
}
```

Both the CLI verb and the MCP `get-page` tool write `body.js`, `bundle.json` and
`meta.json` into `.clio-pages/{schema-name}/` (anchored at the workspace root, or at
`--output-directory`). The `meta.json` persists this state as a `baseline` block
(together with the environment identity), so a later `update-page` / `sync-pages` for the
same environment arms the conflict check from it automatically — no need to pass
`--expected-checksum` by hand. Pass `--expected-checksum` explicitly only to override the
on-disk baseline.

## Synopsis

```bash
clio get-page [options]
```

## Options

```bash
--schema-name                      Page schema name to read

--output-directory                 Directory that anchors the .clio-pages output
                                   tree (body.js / bundle.json / meta.json).
                                   Defaults to the workspace root, or the clio
                                   home root when no workspace is found

--uri                    -u       Application uri

--Password               -p       User password

--Login                  -l       User login (administrator permission required)

--Environment            -e       Environment name

--Maintainer             -m       Maintainer name
```

## Example

```bash
clio get-page --schema-name UsrTodo_FormPage -e dev
return the merged Freedom UI bundle and raw body for UsrTodo_FormPage

clio get-page --schema-name UsrTodo_FormPage -u https://my-creatio -l Supervisor -p Supervisor
read a Freedom UI page using direct connection arguments
```

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#get-page)
