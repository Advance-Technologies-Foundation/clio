# list-entity-client-schemas

## Command Type

    Development commands

## Name

list-entity-client-schemas - Resolve the Classic page-role graph (sections, edit pages, mini pages) of an entity

**Aliases:** `migration-unit-resolve`

## Description

The list-entity-client-schemas command resolves the page-role graph of an entity for a
Classic→Freedom migration: its Classic sections, edit pages (including per-type / typed pages),
and add mini pages. Sections, edit pages, and mini pages include template and `kind` metadata
(`classic`, `freedom`, or `unknown`) so callers do not have to infer page type from names alone.
If the entity metadata does not contain a confirmed base row (`ExtendParent=false`), the command
fails instead of guessing which entity schema UId to use.

This is one level only: the migration workflow recurses into detail entities separately. The
command is pure ESQ over the page-role metadata; it does not read or parse any schema body (use
`get-classic-schema-by-uid` for bodies).

## Synopsis

```bash
clio list-entity-client-schemas [options]
```

## Options

```bash
--entity-name                      Entity schema name, e.g. 'Contact' or 'SupportUnit'
                                   (required)

--uri                    -u       Application uri

--Password               -p       User password

--Login                  -l       User login (administrator permission required)

--Environment            -e       Environment name

--Maintainer             -m       Maintainer name
```

## Example

```bash
clio list-entity-client-schemas --entity-name Contact -e dev
# Resolve Contact's sections, edit pages and mini pages, each classified

clio migration-unit-resolve --entity-name SupportUnit -e dev
# Same using the alias
```

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#list-entity-client-schemas)
