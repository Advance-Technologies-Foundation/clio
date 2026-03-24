# page-update

## Purpose
`page-update` validates and saves the raw JavaScript schema body of a Freedom UI page.

The request contract is unchanged. Even after the `page-get` response upgrade, `page-update`
still accepts only the raw body string.

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

## Examples

Validate an edited body without saving it:
```bash
clio page-update --schema-name UsrTodo_FormPage --body "<raw body>" --dry-run true -e dev
```

Save an edited body to a registered environment:
```bash
clio page-update --schema-name UsrTodo_FormPage --body "<edited raw body>" -e dev
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
- `error`

## Recommended Workflow

1. Use `page-get` to fetch the current page.
2. Modify `raw.body`.
3. Run `page-update --dry-run true` to validate the edited body.
4. Run `page-update` without `--dry-run` to save it.
