# Story 2: Generate a package-level localization schema for application packages

**Feature**: localization-ready-packages
**Issue**: clio#1178
**ADR**: [adr-localization-ready-packages.md](../adr/adr-localization-ready-packages.md)
**Test plan**: [tp-localization-ready-packages.md](../test-plans/tp-localization-ready-packages.md)
**Status**: in-progress
**Size**: M
**Repository**: `Advance-Technologies-Foundation/clio`
**Depends on**: story-localization-ready-packages-1

## As a

Creatio developer creating an application package with Clio

## I want

one valid source-code schema and an injectable adapter for package-level backend localizable values

## So that

I can add translations without reconstructing Creatio schema metadata or coupling consumers to a
Creatio Core concrete type.

## Acceptance criteria

- `clio add-package <name> --as-app` creates `<name>LocalizableStrings` and its resource folder.
- The output has a valid package-derived namespace, identifiers, metadata, ownership comment, caption,
  and one example `LocalizableStrings.*.Value` item.
- `add-package` without `--as-app` and `new-pkg` do not create it.
- `ILocalizableStringResolver` exposes current-culture, strict-culture, and fallback operations without
  exposing `LocalizableString` to consumers.
- `LocalizableStringResolver` is the only generated class that constructs `LocalizableString`, and
  the application composition root registers the interface-to-implementation pair.
- No `Helper` abstraction, static accessor, or package-wide string registry is generated.
- The generated package builds, installs, and resolves the lab-derived runtime example.
- Command docs are current; MCP and ClioRing compatibility are reviewed.

## Definition of done

- All TC-U-* cases for Clio generation pass.
- Targeted Clio tests and analyzer build pass.
- Live install/runtime validation passes on the issue-1178 instance.
- Documentation and required review gates are complete.
