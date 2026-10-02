# find-pkg-by-schema

Find exact schema owners, or resolve entity visibility using package-name and purpose. Use when a schema cannot be opened or a dependency appears missing. hasMore means incomplete results.

```shell
clio find-pkg-by-schema Contact --package-name CrtUIv2 -e my-creatio
```

Requires Creatio package dependency contract v1 and configuration-view permission. Clio discovers capabilities; it does not infer support from a Creatio release number. Pre-feature servers fail explicitly, without a legacy fallback. Retained v1 on future servers works without a Clio upgrade.

| Argument | Meaning | Default |
| --- | --- | --- |
| `schema` | Literal schema name | required |
| `--manager-name` | Optional schema manager | null |
| `--package-name` | Optional package context | null |
| `--purpose` | reference or extend for entity context | "reference" |
| `--contains` | Literal contains search without context | false |
| `--limit` | Maximum 1-200 search rows | 200 |

Use `-e/--environment` to select the registered environment. Standard remote authentication options also apply. No ClioGate package is required. Output is JSON, except `export-pkg-graph --format dot`. Exit 0 means the read succeeded, including a blocked assessment; exit 1 means validation, compatibility or transport failure. No dependency is changed.

Graph reachability does not prove schema visibility or write authorization. Search defaults to exact; `--contains` treats `%` and `_` literally. `hasMore: true` requires narrowing the query before inferring absence. Context resolution supports EntitySchemaManager reference/extend only and cannot combine `--contains` or a nondefault limit. Known metadata reasons and `noKnownBlockers` assessments do not cover dynamic code.

MCP uses the same command name through `clio-run`/`get-tool-contract`, with `environment-name` and the named arguments above. No separate prompt or resource is needed.
