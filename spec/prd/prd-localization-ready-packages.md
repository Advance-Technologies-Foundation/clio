# PRD: Localization-ready generated packages

**Issue**: [#1178](https://github.com/Advance-Technologies-Foundation/clio/issues/1178)
**Status**: Accepted
**Date**: 2026-08-22

## Problem

`clio add-package <name> --as-app` creates an application package without a schema-owned place for
package-level backend localizable values. Developers must discover the required schema metadata,
resource file layout, lookup key, culture behavior, and Freedom UI conventions independently. This
encourages hard-coded text or one package-wide translation container.

## Outcome

A developer can generate an application package, see one ready source-code schema for backend values
that have no natural schema owner, and follow public evidence-backed guidance for backend and Freedom UI
localization. Page, process, and feature strings remain with their owning schemas.

## Functional requirements

- **FR-1** Publish a self-contained public ATF lab containing a Creatio package, stand-free tests,
  Creatio-backed tests, setup instructions, recorded version, results, and a license.
- **FR-2** Demonstrate a backend value owned by a source-code schema and Freedom UI values owned by
  their page schema, in at least two cultures.
- **FR-3** Verify current-culture resolution, explicit culture resolution, default-language fallback,
  a default-only key, and a key missing from every culture.
- **FR-4** `clio add-package <name> --as-app` generates exactly one package-derived source-code schema
  for package-level backend strings that have no natural schema owner.
- **FR-5** The generated schema contains valid identifiers, metadata, resource manager naming, an
  example `LocalizableStrings.<Key>.Value` resource, and an ownership comment.
- **FR-6** The generated application includes `ILocalizableStringResolver`, a concrete adapter over
  Creatio Core's `LocalizableString`, and composition-root registration. Consumers depend on the
  abstraction; only the adapter constructs the concrete platform type.
- **FR-7** `add-package` without `--as-app` and `new-pkg` retain their existing package shape.
- **FR-8** Clio tests verify `--as-app` forwarding and all generated files, macros, ownership text,
  example resource key, and negative paths.
- **FR-9** Clio command help and documentation describe the generated artifact and use the canonical
  switch form.
- **FR-10** Publish one canonical localization guide through `clio-knowledge`, add its name to routing,
  and link detailed Freedom UI mechanics to `page-schema-resources` instead of duplicating them.
- **FR-11** Every non-obvious behavioral claim in guidance is traceable to the public lab, official
  Creatio documentation, or recorded Creatio-backed test output.

## Acceptance

Acceptance is the checklist in issue #1178. Delivery follows this order:

1. Public lab and repeatable evidence.
2. Clio package generation derived from that evidence.
3. Published `clio-knowledge` guidance derived from the lab and final generator behavior.

## Out of scope

- Migrating existing packages or hard-coded strings.
- A centralized container for page, process, schema, or feature resources.
- Application-specific localization registries or a `Helper`-named abstraction.
- Changing the existing rule that permits multiple `--as-app` packages in one workspace.
- Changing generic `add-schema` output unless needed to provide a safe customization seam for the
  generated application localization schema.
