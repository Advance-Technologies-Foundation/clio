# page-list

## Purpose
`page-list` lists Freedom UI page schemas available in a Creatio environment.

Use this command to discover candidate schema names before calling `page-get`.

## Usage
```bash
clio page-list [options]
```

## Options

| Option | Short | Required | Default | Description |
|--------|-------|----------|---------|-------------|
| `--package-name` |  | No |  | Filter pages by package name |
| `--search-pattern` |  | No |  | Filter pages by schema name using a contains match |
| `--limit` |  | No | `50` | Maximum number of results |
| `--Environment` | `-e` | No |  | Registered clio environment name |
| `--uri` | `-u` | No |  | Creatio application URL |
| `--Login` | `-l` | No |  | Creatio user login |
| `--Password` | `-p` | No |  | Creatio user password |
| `--Maintainer` | `-m` | No |  | Maintainer name |

## Output

`page-list` returns a JSON envelope:

```json
{
  "success": true,
  "count": 2,
  "pages": [
    {
      "name": "UsrTodo_FormPage",
      "uId": "guid",
      "packageName": "UsrApp"
    }
  ],
  "error": null
}
```

## Examples

List Freedom UI pages from a registered environment:
```bash
clio page-list -e dev
```

Find form pages by schema name:
```bash
clio page-list --search-pattern FormPage --limit 20 -e dev
```

Limit results to one package:
```bash
clio page-list --package-name UsrApp -e dev
```

Connect directly without a registered environment:
```bash
clio page-list --search-pattern ListPage -u https://my-creatio -l Supervisor -p Supervisor
```

## Recommended Workflow

1. Use `page-list` to find the schema name you need.
2. Call `page-get` with that schema name.
3. Edit `raw.body` from `page-get`.
4. Save the edited body with `page-update`.
