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

Keep each field control bound to the declared view-model attribute from
`viewModelConfig` / `viewModelConfigDiff`. If you add validator or
handler logic on a different attribute for the same field, rebind the
control to that attribute as well so the control, validators, and
handler writes all target the same declared attribute.
If the control is inherited from a parent schema and there is no local
entry for it in `viewConfigDiff`, add a local `merge` for that control
name instead of trying to edit a non-existent local `insert`.

## Validation Rules

**Before editing the body**, understand the validation rules:

- **SCHEMA_CONVERTERS keys** (object form) must follow `VendorPrefix.ConverterName` format
  (e.g., `usr.MyConverter`). Call `clio get-guidance --name page-schema-converters` for details.
- **SCHEMA_HANDLERS** must be an array of `{ request, handler }` entries. Each `request` value
  must follow `VendorPrefix.HandlerName` format (e.g., `crt.HandleViewModelInitRequest`,
  `usr.HandleSomeRequest`). Call `clio get-guidance --name page-schema-handlers` for details.
- **SCHEMA_VALIDATORS keys** (object form) must follow `VendorPrefix.ValidatorName` format
  (e.g., `usr.RequiredValidator`). Call `clio get-guidance --name page-schema-validators` for details.
- **User-visible text must be localizable.** Any `label`, `caption`, `title`, `tooltip`, or
  `placeholder` in `viewConfigDiff` (at any nesting depth) set to an inline string literal is
  **rejected**. Bind it via `$Resources.Strings.<Key>` (or `#ResourceString(<Key>)#` for data-grid
  column captions and validator messages) and register the key's default-language value through
  `--resources`. Binding expressions (any `$`-prefixed value) and non-string values (e.g.
  `placeholder: false`) are not literals and pass. Call `clio get-guidance --name page-schema-resources`
  for the full rule.

A malformed `VendorPrefix.Name` causes a Creatio runtime error:
`"Error when register X. Type property should have format VendorPrefix.TypeName"`.

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
