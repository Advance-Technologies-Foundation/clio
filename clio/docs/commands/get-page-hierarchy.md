# get-page-hierarchy

## Command Type

    Development commands

## Name

get-page-hierarchy - Read the full Freedom UI page replacing-schema chain (root first) with each schema's raw body in one round-trip

**Aliases:** `page-hierarchy-get`

## Description

The get-page-hierarchy command resolves the requested Freedom UI page and returns
EVERY schema in its replacing (designer) chain in a single response, ordered root
first (by hierarchy level) — the same order the deterministic page-bundle merge
consumes. Each entry carries the schema name, UId, package name/UId, whether that
package is locked, schema version, schema type, and its own raw body.

Use this instead of calling `get-page` / `get-client-unit-schema` once per schema
when you need to inspect a whole chain (for example Classic→Freedom migration
discovery): the platform designer service already returns every body in the chain
in one response, so one call replaces N per-schema reads. For a single schema's
editable body (the payload for `update-page`) use `get-page`.

This command is read-only and does not write any files.

## Response Shape

```jsonc
{
  "success": true,
  "schemaName": "UsrApplicants_FormPage", // requested schema
  "rootSchemaName": "BasePageV2",          // base schema (level 0)
  "totalCount": 5,                          // full chain length (before paging)
  "offset": 0,
  "returnedCount": 5,
  "hasMore": false,
  "bodiesOmittedForSize": false,            // true when bodies were auto-dropped (see Paging & Size)
  "warning": null,                          // advisory hint: bodies omitted for size, or package lock state unknown
  "schemas": [
    {
      "hierarchyLevel": 0,                  // 0 = root/base, ascending to the effective leaf
      "schemaName": "…",
      "schemaUId": "…",
      "packageName": "…",
      "packageUId": "…",
      "packageLocked": true,                // SysPackage.InstallType != 0; null = unknown (see Package lock state)
      "schemaVersion": 1,
      "schemaType": "web",                  // web | mobile
      "hasBody": true,
      "bodyLength": 1234,
      "body": "define(…)"                   // omitted when --metadata-only or hasBody is false
    }
    // …
  ]
}
```

## Package lock state

`packageLocked` tells whether the package that owns each layer can receive
design-time writes. It is read from `SysPackage.InstallType` with one query for
the whole chain: `0` is editable (`false`), any other value is locked (`true`),
the flag `lock-package` / `unlock-package` set. It is `null` when the lock state
is unknown, and `warning` then names the cause:

- the `SysPackage` query failed: the chain is still returned and every
  `packageLocked` is `null`;
- `SysPackage` returned no row, or no `InstallType`, for a package: the warning
  lists those package UIds;
- a chain entry has no package UId: the warning lists those schemas.

Treat `null` as unknown, never as unlocked.

The automatic save target of `update-page` (`designPackageName` in `get-page`) is
one of the unlocked layers. When the chain shows another unlocked layer of the
same page, the page is customized in more than one package.

## Paging & Size

The whole chain is returned by default. For a very large chain, use `--offset` /
`--limit` to page over the ordered entries (`totalCount`, `returnedCount` and
`hasMore` describe the window), or `--metadata-only` to drop the raw bodies and
return just the chain structure.

To keep a required-arg-only call on a deep chain within MCP size limits, the bodies
in the selected window are auto-omitted when their summed length exceeds a default
budget (~200k characters). When that happens `bodiesOmittedForSize` is `true` and
`warning` explains how to re-request: use `--metadata-only`, page with
`--offset`/`--limit`, or fetch a single schema's body via `get-page`. Metadata
(including `bodyLength`) is always returned, so the omission is visible and pageable.

## Synopsis

```bash
clio get-page-hierarchy [options]
```

## Options

```bash
--schema-name                      Freedom UI page schema name (any variant in the
                                   replacing chain)

--offset                           Zero-based index of the first chain entry to
                                   return (root first). Default: 0

--limit                            Maximum number of chain entries to return; 0
                                   (default) returns the whole chain from --offset

--metadata-only                    Return chain metadata only (names, UIds, package lock state,
                                   versions) without the raw bodies

--uri                    -u       Application uri

--Password               -p       User password

--Login                  -l       User login (administrator permission required)

--Environment            -e       Environment name

--Maintainer             -m       Maintainer name
```

## Example

```bash
clio get-page-hierarchy --schema-name UsrApplicants_FormPage -e dev
return the whole replacing-schema chain (root first) with every body

clio get-page-hierarchy --schema-name UsrApplicants_FormPage --metadata-only -e dev
list the chain without bodies
```

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#get-page-hierarchy)
