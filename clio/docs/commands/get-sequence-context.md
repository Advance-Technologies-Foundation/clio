# get-sequence-context

Read effective sequence fields and live lookup/configuration choices using DataService.

```sh
clio get-sequence-context -e dev
clio get-sequence-context -e dev --sequence-id <uuid>
```

`--sequence-id` is optional. Use a registered environment with access to schema metadata and the sequence configuration objects. This command reuses the effective-schema reader; its environment requirements apply. It does not install packages, change records, enroll participants or send email.

The JSON result contains `success`, `availability`, `sections` and `limitations`.
`availability` is `present`, `absent` (successful catalog read found no Sequence schema), or `unknown` (catalog could not be read).
Each section reports `complete`, `truncated`, `missing` or `failed`; inspect these before using its data. Choices and columns are capped at 100 per section and the complete response at 200,000 UTF-8 bytes. Requests have a ten-second HTTP timeout and no automatic retries. Use targeted schema or `execute-esq` reads to resolve incomplete sections.

When a sequence ID is supplied, context includes its ordered steps, selected ruleset, delivery schedule and schedule slots. Missing or unreadable prerequisites are explicit; the command does not report them as usable empty configuration.

Schema presence is not proof of permission to activate/enroll/send. Required metadata does not evaluate defaults or dynamic rules. Use native enrollment rather than inserting Active participants or generating activities manually.

MCP: invoke `clio-run` with `command: "get-sequence-context"` and `args: {"environment-name":"dev"}`; add `sequence-id` to inspect a definition. The tool is read-only and discoverable through `get-tool-contract`.
