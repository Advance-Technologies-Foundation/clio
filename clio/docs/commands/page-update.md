# page-update

Update Freedom UI page schema body.


## Usage

```bash
clio page-update [options]
```

## Description

The page-update command validates and saves the raw JavaScript body of a
Freedom UI page schema. Pass the full body string directly, typically
after reading raw.body from page-get.

When the body contains #ResourceString(key)# macros, page-update can
register missing child-schema localizableStrings before saving. Pass
--resources when you need explicit captions, or let clio derive captions
automatically for missing Usr* keys.

## Examples

```bash
clio page-update --schema-name UsrTodo_FormPage --body "<raw body>" --dry-run true -e dev
validate a raw Freedom UI body without saving it

clio page-update --schema-name UsrTodo_FormPage --body "<edited raw body>" -e dev
save the edited raw Freedom UI body to the registered dev environment

clio page-update --schema-name UsrTodo_FormPage --body "<edited raw body>" --resources "{\"UsrDetailsTab_caption\":\"Details\"}" -e dev
save the page and register the missing child-schema localizable string
```

## Options

```bash
--schema-name                      Page schema name to update

--body                             Full raw JavaScript schema body

--dry-run                          Validate only and do not save

--resources                        Valid JSON object of resource key-value
pairs for #ResourceString(key)# macros
Malformed JSON is rejected during
validation

--uri                    -u       Application uri

--Password               -p       User password

--Login                  -l       User login (administrator permission required)

--Environment            -e       Environment name

--Maintainer             -m       Maintainer name
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
--restartEnvironment
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

## Command Type

    Development commands

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#page-update)
