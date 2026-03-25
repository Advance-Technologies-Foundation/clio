# page-update

## Purpose
`page-update` validates and saves the raw JavaScript schema body of a Freedom UI page.

Use `raw.body` from `page-get` as the editable payload. When the body contains
`#ResourceString(key)#` macros, `page-update` can also register missing child-schema
`localizableStrings` entries before saving.

## Usage
```bash
clio page-update --schema-name <SCHEMA_NAME> --body "<RAW_BODY>" [options]
```

## Options

| Option | Short | Required | Default | Description |
|--------|-------|----------|---------|-------------|
| `--schema-name` |  | Yes |  | Freedom UI page schema name |
| `--body` |  | Yes |  | Full raw JavaScript schema body |
| `--dry-run` |  | No | `false` | Validate only and do not save |
| `--resources` |  | No |  | Valid JSON object string with resource key-value pairs for `#ResourceString(key)#` macros |
| `--Environment` | `-e` | No |  | Registered clio environment name |
| `--uri` | `-u` | No |  | Creatio application URL |
| `--Login` | `-l` | No |  | Creatio user login |
| `--Password` | `-p` | No |  | Creatio user password |
| `--Maintainer` | `-m` | No |  | Maintainer name |

## Input Expectations

Pass the entire raw page body, including the Freedom UI schema markers such as:

- `SCHEMA_DEPS`
- `SCHEMA_ARGS`
- `SCHEMA_VIEW_CONFIG_DIFF`
- `SCHEMA_VIEW_MODEL_CONFIG_DIFF`
- `SCHEMA_MODEL_CONFIG_DIFF`
- `SCHEMA_HANDLERS`
- `SCHEMA_CONVERTERS`
- `SCHEMA_VALIDATORS`

The recommended source for this payload is `raw.body` returned by `page-get`.

## Resource Registration

When the body contains `#ResourceString(key)#` macros, `page-update` scans the body and
ensures the child schema contains the required `localizableStrings` entries before saving.

- Pass `--resources` with a JSON object string such as `{"UsrDetailsTab_caption":"Details"}` when you need exact captions.
- Malformed `--resources` payloads fail validation instead of being ignored.
- Missing `Usr*` keys without an explicit value are auto-derived from the key name.
- Parent/template resources that do not start with `Usr` are not added to the child schema.
- Inherited non-`Usr` resources are stripped from the child schema before save to avoid duplicate-entry errors.

## Examples

Validate an edited body without saving it:
```bash
clio page-update --schema-name UsrTodo_FormPage --body "<raw body>" --dry-run true -e dev
```

Save an edited body to a registered environment:
```bash
clio page-update --schema-name UsrTodo_FormPage --body "<edited raw body>" -e dev
```

Save an edited body and register explicit page resources:
```bash
clio page-update --schema-name UsrTodo_FormPage --body "<edited raw body>" --resources '{"UsrDetailsTab_caption":"Details"}' -e dev
```

Connect directly without a registered environment:
```bash
clio page-update --schema-name UsrTodo_FormPage --body "<edited raw body>" -u https://my-creatio -l Supervisor -p Supervisor
```

## Output

On success the command returns a JSON envelope with:

- `success`
- `schemaName`
- `bodyLength`
- `dryRun`
- `resourcesRegistered`
- `registeredResourceKeys`
- `error`

## Recommended Workflow

1. Use `page-get` to fetch the current page.
2. Modify `raw.body`.
3. Pass `--resources` when the edited body adds or changes `#ResourceString(key)#` macros and you need explicit captions. The payload must be a valid JSON object string.
4. Run `page-update --dry-run true` to validate the edited body.
5. Run `page-update` without `--dry-run` to save it.
