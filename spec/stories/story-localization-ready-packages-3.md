# Story 3: Publish localization guidance through clio-knowledge

**Feature**: localization-ready-packages
**Issue**: clio#1178
**ADR**: [adr-localization-ready-packages.md](../adr/adr-localization-ready-packages.md)
**Test plan**: [tp-localization-ready-packages.md](../test-plans/tp-localization-ready-packages.md)
**Status**: in-progress
**Size**: M
**Repository**: `Advance-Technologies-Foundation/clio-knowledge`
**Depends on**: story-localization-ready-packages-1, story-localization-ready-packages-2

## As a

Clio guidance consumer

## I want

one routed, evidence-backed guide for backend and cross-schema localization decisions

## So that

I can define, translate, resolve, test, and troubleshoot localizable values correctly.

## Acceptance criteria

- `localizable-values` is registered and discoverable from routing.
- The guide teaches schema ownership and limits the generated schema to package-level backend values
  with no natural owner.
- Backend strict/fallback behavior and both test boundaries are linked to public evidence.
- Freedom UI instructions link to `page-schema-resources` and do not duplicate its rules.
- Catalog/version generation and producer contract tests pass.

## Definition of done

- Every non-obvious claim maps to lab evidence or an authoritative public source.
- The guide works without machine-local or private source access.
- Clio-knowledge validation and review gates pass.
