---
description: a build-path flow condition references a parameter BY NAME - [#Amount#] or [#Element.Parameter#] - and CrtProcessBuilder expands it to the UId meta-path after the schema is built, because on create-business-process those UIds do not exist until the same call creates them; the expansion is narrow by design and passes every platform macro family through untouched
applies-to:
  - clio/Command/McpServer/Tools/ProcessDesigner/CreateBusinessProcessTool.cs
  - clio/CrtProcessBuilder/CrtProcessBuilder.gz
ticket: ENG-91853
date: 2026-09-06
---

**What is true** — on the BUILD path, and only there, a flow condition may address a parameter by NAME:
`[#Amount#]` for a process parameter and `[#Read.ResultEntity#]` for an element output. The package
expands each to the UId meta-path the platform actually evaluates, in a pass over the whole schema that
runs after every parameter and element exists. On the MODIFY path there is no expansion and none is
needed: the UIds exist by then and `describe-business-process` reports them.

**Why it has to work this way** — a condition is evaluated through the meta-path and never through the
name, and on `create-business-process` those UIds do not exist when the caller writes the request: the
parameters and elements are created by that same call, and `ProcessParameterDescriptor` carries no
`uid` field to pre-declare one with. Without the expansion, `flows[].condition` serves only conditions
that reference no parameter. Measured over the shipped 7.8.0 corpus — 1 405 conditional flows, 1 402
with a stored expression, ~341 of them decoding to an empty one the runtime replaces with `true`:

| Expression shape | Count | Buildable before the expansion |
|---|---|---|
| element output `[Element:{uid}].[Parameter:{uid}]` | 487 | no |
| process parameter `[Parameter:{uid}]` | 445 | no |
| literal / call into the schema's own generated code | 92 | rarely |
| `[#SysSettings.Code<Type>#]` | 37 | yes |

932 of the 1 061 non-empty expressions — 87.8% — carried a UId that could not exist yet, and
`SysSettings` at 3.5% was the whole of what worked. A clean-room agent run confirmed the consequence
before the fix: the executor never used `flows[].condition` once and routed every branch through a
later `modify-business-process`.

**The rule is deliberately narrow toward doing NOTHING.** Only a bare identifier, or a dotted one whose
head is an element of this schema, is touched. Everything else passes through — a list of known macro
families would refuse the next one the platform adds. Two consequences worth knowing:

- A **bare** name that resolves to nothing is REFUSED, naming the flow by its endpoints and listing the
  parameters that exist. That is safe because no platform macro family is a single identifier, and it
  replaces the platform's own `Formula value error: Expression expected (at index 0)`, which names
  neither the flow nor the name and aborts the entire build.
- The **short** meta-path spelling `[#[Parameter:{uid}]#]` carries no dot, so it would read as a bare
  name and be refused. It is passed through by an explicit bracket check, because the platform emits a
  parameter reference both with and without the `[IsOwnerSchema:false].[IsSchema:false].` prefix.

**What breaks if you ignore it** — three things, all of which were live during this change:

- **Moving the pass earlier.** It must run after the declarative phase, not inside `BuildGraph`: a
  parameter carrying `typeFromElement` is added in `ApplyDeclarativeContent`, so a condition naming one
  resolves against a schema that does not have it yet, and the build is refused for a parameter the
  request does declare.
- **Widening the rule** to interpret any `[# … #]` token. `[#SysSettings.Code<Int32>#]` is the one
  family that worked before this change; rewriting it trades one gap for another.
- **Reaching for a regex** to walk the tokens. `ProcessMetaPath` deleted its `MacroTokenPattern` for
  exactly this job, because `\[#(?<body>.*?)#\]` backtracks quadratically on text with many `[#` and
  no `#]`. The walk is `IndexOf` pairs with `cursor = close + 2`.
