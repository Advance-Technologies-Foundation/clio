# ADR: Schema-level export and import

- Status: Accepted
- Date: 2026-08-19
- Issue: [#1113](https://github.com/Advance-Technologies-Foundation/clio/issues/1113)
- Related PRD: [prd-schema-level-export-import.md](../prd/prd-schema-level-export-import.md)

## Context

There is no way to move a single schema between Creatio environments. The only available path is
`pull-pkg` / `push-pkg`, which carries the whole package. On a customer production site that is a real
risk: the package holds every customization the customer has, so installing it to deliver a one-schema
fix can overwrite unrelated work that exists only on that environment.

The gap is widest for **addon** schemas (`AddonSchemaManager` — business rules, related pages), which
have no read surface at all today.

## Decision

### 1. Delegate the payload to the platform schema exporter, do not invent a wire format

`Terrasoft.Core.SchemaImporter` exposes exactly the two operations this feature needs, and they are
**type-agnostic**:

```
public static string ExportSchema(Guid schemaId, SystemUserConnection systemUserConnection)
public static string ImportSchemaToWorkspace(string schemaData, Guid packageUId, UserConnection userConnection)
```

They are present in CreatioSDK `8.0.3` and `8.1.4` alike. **Their shape is not stable across cores**, which the
implementation had to absorb: on a `10.1.473` stand `ExportSchema` is still a public static, while
`ImportSchemaToWorkspace` does not exist as a public static at all and the operation is served only by the
explicit implementation of `ISchemaImporter.ImportSchema(string, Guid)` — an interface whose type is public but
whose members are not accessible outside the core assembly. Both calls are therefore late-bound, static entry
point first and interface second, each isolated in its own `[MethodImpl(MethodImplOptions.NoInlining)]` method:
a missing member is raised while the CALLING method is jitted, so an in-method try/catch never sees it and WCF
answers with an opaque "Request Error" instead of a diagnosable message.
The exported payload is a single self-describing JSON document carrying `UId`, `ManagerName`, `Name`,
`Caption`, `ExtendParent`, `DenyExtending`, `Description`, the schema `MetaData`, a `Properties` array and
a `LocalizableValues` array — i.e. the descriptor, metadata, properties and resources the issue asks for,
already in one artifact. On import the platform owns the extending-versus-standalone decision, the
duplicate-name check, schema locking and resource replacement.

Two alternatives were rejected:

- **Per-designer round-trip** (`SourceCodeSchemaDesignerService`, `ClientUnitSchemaDesignerService`,
  `AddonSchemaDesignerService`, …). Needs one contract per schema kind, and several kinds — entity
  schemas, business processes, data-bound schemas — have no safe `GetSchema`/`SaveSchema` pair at all.
  It cannot deliver the requested breadth.
- **Reading the file-system representation** (`Pkg/<Package>/Schemas/<Name>/descriptor.json`, …). That
  directory is populated only under File Design Mode, and the motivating scenario is a customer
  production site where enabling FSM is precisely the heavyweight operation this feature exists to avoid.
  `cliogate`'s existing `PackageExplorer` is also rooted at `Pkg/<Package>/Files`, so `Schemas/` is out of
  its reach anyway.

### 2. Three new ClioGate endpoints

`SysSchema` carries restricted NUI security, so the lookup uses `Terrasoft.Core.DB.Select` rather than an
ESQ — a name lookup that returned nothing because of permissions would be indistinguishable from a schema
that does not exist. Per `AGENTS.md`, all three live at `/rest/CreatioApiGateway/<MethodName>` and call
`CheckCanManageSolution()` first.

| Endpoint | Purpose |
|---|---|
| `FindSchemaLayers` | Read-only. Lists every package layer carrying a schema of that name. |
| `ExportSchema` | Resolves exactly one layer, returns the platform payload. |
| `ImportSchema` | Writes a payload into a named package. |

### 3. Ambiguity is reported, never guessed

A schema name is unique only per (manager, package) pair. `ExportSchema` refuses a name that matches more
than one layer and returns the matching layers in `candidates`, so the caller can retry with an explicit
package. This is deliberately the opposite of `delete-schema --remote`, which resolves by name only and can
pick a different layer than intended. **That bug is not fixed here** — changing resolution on a destructive
command is a separate behaviour change and belongs in its own review.

### 4. The exported bundle is a folder: one authoritative file plus reviewable projections

```
<SchemaName>/
  descriptor.json     provenance and identity (clio-authored)
  schema-data.json    the verbatim platform payload — the ONLY input import consumes
  metadata.json       projection: the MetaData document, expanded for reading
  properties.json     projection: the Properties array
  resources/          projection: LocalizableValues grouped per culture
```

Only `schema-data.json` is authoritative. The projections exist because the issue's real requirement is a
*small reviewable artifact*, and a single escaped-JSON blob is not reviewable. Import reads
`schema-data.json` and ignores the projections, so a hand-edited projection can never silently become the
thing that ships.

### 5. Identity is preserved on import

The payload carries the source `UId` and the platform importer honours it, so an imported schema is the
*same* schema on the target rather than a divergent copy — later updates line up instead of colliding.
This directly addresses the defect that motivated the issue (a foreign `Schema.UId` in addon metadata).

### 6. A same-name schema in a different package refuses by default

Before writing, `import-schema` calls `FindSchemaLayers`. If the schema exists only in the target package,
the import is a **replace** and proceeds. If it exists in some *other* package, the import would create a
new layer — sometimes intended, and sometimes exactly the
`IU_Name_Manager_Package` duplicate-key defect from the issue — so it fails, naming the packages found,
unless `--allow-new-layer` is passed.

### 7. `--dry-run` ships in v1

`import-schema --dry-run` resolves the target, reports create-versus-replace and the layers found, and
returns without calling `ImportSchema`. A cautious operator on a production site needs to see what an
import would do before it does it; deferring this would leave the feature untrustworthy for its own
motivating scenario.

### 8. The bundle's projections are parsed with Newtonsoft, not `System.Text.Json`

The platform payload embeds the schema metadata as a JSON **string** containing raw CR/LF control characters,
which RFC 8259 forbids and `System.Text.Json` refuses. Parsing the payload with it would fail on every real
export and silently skip every projection — the reviewability the bundle exists for. Newtonsoft accepts it.

## Consequences

- Requires cliogate `2.0.0.46` on the target environment. Both commands declare it with
  `[RequiresPackage]`, so an outdated environment is reported rather than failing obscurely.
- `export-schema` reads the environment but always writes a local bundle folder, so its MCP annotation is
  `ReadOnly=false, Destructive=false` — the same classification `get-schema` / `get-page` carry, and the one the
  durable-invocation gate keys on. Its `--destination` runs through `OutputPathConfinement` before any network
  call, because an MCP agent can supply it. `import-schema` writes configuration and is classified destructive.
- Neither option takes a `-p` short form: `EnvironmentOptions` already binds `-p` to `--password`, and a
  duplicate short name makes the parser reject the whole verb with
  `Sequence contains more than one matching element`.
- Breadth is genuinely uniform: any schema kind the platform exporter supports round-trips, addons
  included. What the platform refuses, it refuses with its own message rather than a clio-invented one.
- An imported schema that requires compilation or a database-structure update still needs the usual
  follow-up (`compile-configuration`, `update-db-structure`); import does not perform them, and the
  command output says so.
