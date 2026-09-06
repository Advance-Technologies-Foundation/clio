---
description: the MCP SDK builds a tool schema's `required` array from the non-nullable, non-defaulted positional parameters of the DERIVED record STJ binds - relaxing a base record or adding [Required]-free aliases changes nothing
applies-to:
  - clio/Command/McpServer/Tools/
  - clio.tests/Command/McpServer/ColumnIdentityEmittedSchemaTests.cs
  - clio.tests/Command/McpServer/EmittedSchemaProbe.cs
  - clio.tests/Command/McpServer/EmittedSchemaRequiredContractTests.cs
  - clio.tests/Command/McpServer/ConnectionAlternativeDispatchTests.cs
date: 2026-08-19
---

**What is true** — for every MCP tool argument record, the emitted JSON schema's `required` array is
derived from the record the SDK actually binds: its own positional parameters that are non-nullable and
have no default. A base record or an interface higher up the hierarchy has no effect, and neither does
the presence or absence of a `[Required]` attribute. To make an aliased or optional field genuinely
optional it must be declared `string? X = null` on the concrete record, on **every** concrete record
that surfaces it. One argument type frequently backs more than one emitted surface, for example an
operation record used both by its own tool and by a batch tool that embeds it.

**Why it is this way** — the schema generator reflects over the exact type it will deserialize into,
which is the derived record; parameter nullability and defaults are the only signal it reads.

**What breaks if you ignore it** — a strict MCP client validates against the published schema and
refuses to send the payload the tool documentation advertises, so a caller following the contract sees
a client-side rejection and no server log at all. Substring assertions over the serialized schema do
not catch it either: element order, whitespace, or one extra required field all keep a
`NotContain("\"required\":[...]")` assertion green while the relaxation is reverted. Assert by
navigating the emitted schema, as `ColumnIdentityEmittedSchemaTests` does.

**Registry-wide guard (issue #965).** After the third recurrence of this defect (PR #984, ENG-93347,
issue #965) the guard is no longer written per tool. `EmittedSchemaRequiredContractTests` walks EVERY
registered tool's emitted schema and fails when (a) an object advertising BOTH `environment-name` and
`uri` lists any connection field as required — the runtime accepts either path, so neither is
mandatory — or (b) a required property's own `description` begins with "Optional". Add a new tool
without a default on an optional parameter and that guard, not a reviewer, is what catches it.

**Two surfaces, and only comparing them catches the third failure mode (PR #1396 review).** A tool has
TWO published argument contracts: the emitted `tools/list` schema, and the `get-tool-contract` envelope
— curated by hand in `ToolContractCatalog.Contracts` for a resident tool, derived from the emitted
schema for everything else. Guard (a) and guard (b) above read the emitted schema ONLY, so relaxing a
record while leaving the handwritten contract demanding the same field passes both and leaves the two
surfaces contradicting each other. That is exactly what happened to `get-entity-schema-properties`: the
record was relaxed, the curated contract still listed `package-name` as required, and an agent planning
the call from `get-tool-contract` therefore always sent it — which switches the tool from the merged
all-packages view (49 own columns) to a single package layer (0 own columns) and silently hides every
column it was looking for. `CuratedContracts_Should_AgreeWithEmittedSchema_ForResidentTools` is the
comparison; the scope is the resident set because those are the tools whose emitted schema a strict
client actually validates against (a long-tail tool is dispatched through `clio-run`, so `clio-run`'s
schema is what gates the payload). `get-guidance` is the one documented exception: its `name` parameter
is nullable so a legacy-alias payload reaches the tool and returns a typed answer instead of an SDK
binding error, while the curated contract keeps stating that a name is what the caller must supply.

**A bare property-name pair is not a connection selector.** The registry-derived contract attaches the
`environment-name` OR `uri`/`login`/`password` alternative when a schema advertises both names. On
`reg-web-app` those two names are the record being WRITTEN, not a route to an existing environment, and
the tool rejects a credential-only payload with a typed exit code 1. The discriminator is the
environment-registration surface (`active-environment` / `add-from-iis`), NOT description text:
`get-related-page-addon`, `list-printables` and `MobilePageConversionGuideArgs` all carry custom `uri`
wording and are genuine fallbacks.
