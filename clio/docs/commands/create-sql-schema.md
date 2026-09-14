# create-sql-schema

## Command Type

    Development commands

## Name

create-sql-schema - Create a new SQL script schema on a remote Creatio environment

**Aliases:** `sql-schema-create`

## Description

The create-sql-schema command creates a new SQL script schema on a remote Creatio environment
via ScriptSchemaDesignerService. The schema is saved directly to the server; no local
workspace files are created.

The schema-name must start with a letter and contain only letters, digits, or underscores.
The name must be unique within the environment.

## Synopsis

```bash
clio create-sql-schema [options]
```

## Options

```bash
--schema-name                      New SQL schema name (required)

--package-name                     Target package name that will own the new schema (required)

--caption                          Optional display caption; defaults to schema-name

--description                      Optional schema description

--caption-culture                  Override the culture for the generated schema
                                   caption (e.g. en-US, uk-UA). Precedence:
                                   override > the connected user's profile
                                   culture (see get-user-culture) > en-US.

--uri                    -u       Application uri

--Password               -p       User password

--Login                  -l       User login (administrator permission required)

--Environment            -e       Environment name

--Maintainer             -m       Maintainer name
```

## Example

```bash
clio create-sql-schema --schema-name UsrCleanupStaleRows --package-name Custom -e dev
# Create UsrCleanupStaleRows in the Custom package on the dev environment

clio create-sql-schema --schema-name UsrCleanupStaleRows --package-name Custom --caption "Cleanup stale rows" -e dev
# Create with a display caption

clio sql-schema-create --schema-name UsrCleanupStaleRows --package-name Custom --description "Nightly cleanup" -e dev
# Create with a description using the alias
```

## Notes

- The schema caption is stored under the resolved culture (`--caption-culture` override > the connected user's profile culture > `en-US`). A caption whose script does not match a Latin-script culture (for example Cyrillic under `en-US`) is rejected with an actionable error; pass `--caption-culture` to author the caption in a specific language.

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#create-sql-schema)

## Notes

`ScriptSchemaDesignerService` must be served by the target environment. When it is not, the
endpoint answers with an empty body or an HTML error page. clio reports which service, operation
and URL answered instead of a raw JSON parser message; because clio's synchronous client does not
expose the HTTP status, the message states that the status is unknown rather than quoting one.

When the save answer itself is unusable, clio reads the schema back before reporting the outcome:
a schema that was in fact created is reported as a success, a schema that is absent as a failure,
and a read-back that itself fails as an unverified outcome that must be checked manually.
