# check-pkg-dependency

Preview add or remove dependency rules without a write. noKnownBlockers is limited to checkedKinds, never a guarantee for dynamic code or write authorization.

```shell
clio check-pkg-dependency CrtUIv2 CrtNUI --action remove -e my-creatio
```

Requires Creatio package dependency contract v1 and configuration-view permission. Clio discovers capabilities; it does not infer support from a Creatio release number. Pre-feature servers fail explicitly, without a legacy fallback. Retained v1 on future servers works without a Clio upgrade.

| Argument | Meaning | Default |
| --- | --- | --- |
| `from` | Depending package name or UId | required |
| `to` | Dependency package name or UId | required |
| `--action` | add or remove | "add" |

Use `-e/--environment` to select the registered environment. Standard remote authentication options also apply. No ClioGate package is required. Output is JSON, except `export-pkg-graph --format dot`. Exit 0 means the read succeeded, including a blocked assessment; exit 1 means validation, compatibility or transport failure. No dependency is changed.

Graph reachability does not prove schema visibility or write authorization. Search defaults to exact; `--contains` treats `%` and `_` literally. `hasMore: true` requires narrowing the query before inferring absence. Context resolution supports EntitySchemaManager reference/extend only and cannot combine `--contains` or a nondefault limit. Known metadata reasons and `noKnownBlockers` assessments do not cover JavaScript source imports or dynamic code. A raw AMD import can exist without a registered metadata dependency; inspect source and test before removing an edge.

MCP uses the same command name through `clio-run`/`get-tool-contract`, with `environment-name` and the named arguments above. No separate prompt or resource is needed.
