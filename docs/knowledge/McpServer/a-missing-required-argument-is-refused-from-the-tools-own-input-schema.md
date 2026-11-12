---
description: a call that leaves out a required argument is refused before the SDK binds it ("argument '<name>' for MCP tool '<tool>' is required and was not sent."), on the direct path and inside clio-run; what is required comes from the tool's own input schema, whose `required` list is exactly the SDK binder's rule (no default value, a nullable parameter included), and names are matched ordinally as the binder matches them
applies-to:
  - clio/Command/McpServer/McpToolErrorFilter.MissingArgument.cs
  - clio/Command/McpServer/McpToolErrorFilter.cs
  - clio/Command/McpServer/Tools/ClioRunTool.cs
  - clio/Command/McpServer/Tools/McpToolRegistrySchemaContract.cs
  - clio.tests/Command/McpServer/McpToolErrorFilterMissingArgumentTests.cs
date: 2026-11-12
---

**What is true** — `McpToolErrorFilter.TryCreateMissingArgumentError` refuses a call whose arguments leave out
a name the tool's input schema lists as `required`. It runs after every other argument check, in two places:
`HandleCallToolErrorsCore` for a direct call, before routing, and `ClioRunExecutor.DispatchAsync` for a
`clio-run` dispatch and the durable long-tail handler (see
[[argument-preflight-placement-differs-per-dispatch-path]]). The refusal reads:

```
invalid-parameter-type: argument 'environmentName' for MCP tool 'get-fsm-mode' is required and was not sent.
Unknown argument 'environment-name' (did you mean 'environmentName'?). About 'environmentName': ...
get-fsm-mode takes: environmentName (string, required). Argument names are matched exactly, case included.
Nothing ran.
```

Two facts about the SDK (ModelContextProtocol on Microsoft.Extensions.AI) make the schema the right source:

1. The schema's `required` list is the binder's rule. A parameter with no default value is required in both,
   and that includes a nullable one with no default (`string? note`). This is the rule for the tool method's
   own parameters, the top level of the schema. Inside an args record, the nested `required` list leaves a
   nullable member out ([[emitted-schema-required-comes-from-the-record-stj-binds]]). This check never reads
   the nested list.
2. The binder looks a flat parameter up by its exact name. It copies the arguments into its own ordinal
   dictionary, so `EnvironmentName` does not bind `environmentName`. An args record's MEMBERS are matched
   case-insensitively by System.Text.Json; that is a different layer.

So the refusal fires exactly where the SDK would have thrown "The arguments dictionary is missing a value for
the required parameter '<name>'. (Parameter 'arguments')", and nowhere else. A parameter the SDK fills from
services or the request context is not in the schema and is never reported missing. An explicit JSON `null`
is a present key: the SDK binds it as null, and so the refusal leaves it to the null-argument check.

`McpToolErrorFilterMissingArgumentTests.Sdk_ShouldRequireExactlyWhatTheSchemaMarksRequired_AndBindFlatParametersByExactName`
pins both facts.

**Why it is this way** — about a dozen tools take flat camelCase parameters (`get-fsm-mode`, `start`, `stop`,
`restart`, `clear-redis`, `load-packages`, the identity assertions, `link-from-repository`), while every
args-record tool spells its fields in kebab-case. An agent sends `environment-name` out of habit. The SDK's
text named the parameter but not the wrong key. Through `clio-run` it arrived as "tool 'get-fsm-mode' failed:
...", which reads like a failure inside clio. Found in an AI app-building run on 12 Nov 2026.

The key is not renamed silently. Accepting `environment-name` for `environmentName` would widen the contract
with an alias `tools/list` does not advertise. The refusal shows the rename, and the next call is right.

**What breaks if you ignore it** — reading "required" from the method instead (for example `[Required]` or
nullability) disagrees with the binder. A nullable parameter with no default is required to the binder, and a
DI-bound parameter is not an argument at all, so the refusal would either miss calls or refuse good ones.
Matching names case-insensitively would let `EnvironmentName` through to the SDK's text again. If an SDK
upgrade changes either fact, the pin test fails first. The lead sentence is part of the refusal contract in
[[a-binding-refusal-keeps-its-code-and-lead-sentence-stable-and-its-hints-free]].
