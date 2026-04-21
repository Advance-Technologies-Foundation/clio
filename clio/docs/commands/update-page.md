# update-page

## Command Type

    Development commands

## Name

update-page - Update the raw schema body of a Freedom UI page

**Aliases:** `page-update`

## Description

The update-page command validates and saves the raw JavaScript body of a
Freedom UI page schema. Pass the full body string directly, typically
after reading raw.body from get-page.

When the body contains #ResourceString(key)# macros, update-page can
register missing child-schema localizableStrings before saving. Pass
--resources when you need explicit captions, or let clio derive captions
automatically for missing Usr* keys.

Use --optional-properties to merge custom key-value pairs into the schema's
`optionalProperties` array (for example to set `entitySchemaName`).

## Synopsis

```bash
clio update-page [options]
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

--optional-properties              JSON array of {key, value} objects to
merge into schema optionalProperties,
e.g. '[{"key":"entitySchemaName","value":"UsrMyEntity"}]'

--uri                    -u       Application uri

--Password               -p       User password

--Login                  -l       User login (administrator permission required)

--Environment            -e       Environment name

--Maintainer             -m       Maintainer name
```

## Example

```bash
clio update-page --schema-name UsrTodo_FormPage --body "<raw body>" --dry-run true -e dev
validate a raw Freedom UI body without saving it

clio update-page --schema-name UsrTodo_FormPage --body "<edited raw body>" -e dev
save the edited raw Freedom UI body to the registered dev environment

clio update-page --schema-name UsrTodo_FormPage --body "<edited raw body>" --resources "{\"UsrDetailsTab_caption\":\"Details\"}" -e dev
save the page and register the missing child-schema localizable string

clio update-page --schema-name UsrTodo_FormPage --body "<edited raw body>" --optional-properties "[{\"key\":\"entitySchemaName\",\"value\":\"UsrTodo\"}]" -e dev
save the page and merge custom optional properties into the schema
```

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#update-page)
