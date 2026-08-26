# ADR: Generate a schema-owned localization sample for application packages

**Status**: Accepted
**Date**: 2026-08-22
**Issue**: [#1178](https://github.com/Advance-Technologies-Foundation/clio/issues/1178)

## Context

Creatio resource managers are schema-scoped. Backend code resolves schema resources through
`UserConnection.Workspace.ResourceStorage` and keys shaped as
`LocalizableStrings.<Key>.Value`. `LocalizableValue.Value` uses the current culture and Creatio's
fallback path; explicit strict and fallback methods must be tested separately.

Clio already has `ISchemaBuilder` and source-code schema metadata templates. The generic template has
only a schema caption, uses the free-text package maintainer in the C# namespace, and has no way to add
localization-specific documentation or a sample resource without changing every `add-schema` result.

## Decisions

### D1. Evidence lives in a public lab

Create `Advance-Technologies-Foundation/creatio-localization-lab`. Its package owns one backend sample
in a source-code schema and UI samples in a Freedom UI page schema. Stand-free tests validate repository
assets and ownership; integration tests call the installed package and validate Creatio resource loading.
The lab records Creatio 10.1.585 evidence but keeps setup parameterized.

### D2. The generated artifact is per package and narrowly owned

For `add-package <name> --as-app`, create `<name>LocalizableStrings` with:

- complete source-code schema metadata;
- `resource.en-US.xml` containing the schema caption and one example
  `LocalizableStrings.PackageLevelExample.Value` item;
- a source comment stating that this schema is only for package-level backend values with no natural
  schema owner and that page, process, and other schema resources stay with their owners.

Do not create the schema for `asApp == false` or the `new-pkg` overload.

### D3. Reuse `ISchemaBuilder` and generate an injectable platform adapter

Extend the existing source-code schema template seam only enough to let `PackageCreator` provide:

- the package root namespace (`<name>App`), avoiding the free-text maintainer namespace;
- localization-specific class documentation;
- the single example localizable string.

Use a data-only options record if needed. Do not add an `I*Helper`, static accessor, or package-wide
string registry. Generate `ILocalizableStringResolver` with explicit
current-culture, strict-culture, and fallback operations; implement it in
`LocalizableStringResolver`; and register the pair in the application composition root. Only
the adapter constructs `Terrasoft.Common.LocalizableString`. The abstraction does not reference the
generated schema, so consumers still choose the correct schema owner for every value.

### D4. Preserve schema ownership in guidance

The new `localizable-values` guide owns backend resource definition, lookup, fallback, tests, and the
cross-schema ownership rule. The existing `page-schema-resources` guide remains the canonical owner of
Freedom UI binding syntax and page authoring details. Routing lists guide names only.

### D5. Keep command and MCP boundaries narrow

Update `add-package` CLI help, command documentation, command index, and anchor review. The MCP argument
contract is unchanged. Review the MCP tool, prompt, templates, unit tests, and E2E surface; change them
only if the existing contract becomes inaccurate. No ClioRing-consumed contract changes.

### D6. Validate at three levels

1. Clio unit tests validate `--as-app` propagation and generated files/content.
2. The public lab's stand-free tests validate resource inventory and ownership.
3. Lab integration tests install and exercise actual schema resource loading on Creatio.

## Rejected alternatives

- **One schema for every package string**: violates schema ownership and becomes a merge hotspot.
- **A `Helper` interface or static accessor**: hides dependencies and cannot be replaced cleanly in tests.
- **Direct `LocalizableString` construction in consumers**: couples application and transport code to a
  Creatio Core concrete type and makes focused unit testing harder.
- **Put the sample in the generic source-code template**: changes every `add-schema` result and teaches
  unrelated schemas to carry an unused resource.
- **Duplicate Freedom UI instructions in the new guide**: creates two canonical owners for the same
  binding rules.
- **Repair multi-app descriptor behavior here**: unrelated, test-locked behavior outside issue #1178.

## Consequences

Application packages gain one small, discoverable backend localization owner and one injectable adapter
over Creatio's concrete localizable-string primitive. A harmless example key is present until a developer
replaces it. The implementation must keep template customization explicit and must prove that the generated
standalone package builds and installs without compiling the schema twice.
