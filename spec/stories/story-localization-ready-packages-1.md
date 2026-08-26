# Story 1: Publish the Creatio localization lab

**Feature**: localization-ready-packages
**Issue**: clio#1178
**ADR**: [adr-localization-ready-packages.md](../adr/adr-localization-ready-packages.md)
**Test plan**: [tp-localization-ready-packages.md](../test-plans/tp-localization-ready-packages.md)
**Status**: done
**Size**: M
**Repository**: `Advance-Technologies-Foundation/creatio-localization-lab`

## As a

Creatio developer learning localization

## I want

a public package and executable tests that demonstrate schema-owned backend and Freedom UI resources

## So that

I can reproduce culture selection, missing-key, and fallback behavior without private source access.

## Acceptance criteria

- Public repository, license, setup/deploy/test instructions, and parameterized environment inputs.
- Source-code schema and Freedom UI page own separate localizable resources in en-US and a secondary
  culture.
- Stand-free tests run from a clean checkout.
- The lab web service validates transport input and delegates to a DI-resolved domain service; only the
  concrete localization adapter constructs Creatio Core's `LocalizableString`.
- Creatio-backed tests verify current/default/secondary/default-only/missing/fallback cases.
- Evidence records Creatio version, inputs, expected values, actual values, and verification date.

## Definition of done

- All lab tests pass at their documented boundary.
- The lab installs and runs on the issue-1178 Creatio instance.
- Public instructions contain no credentials, private paths, or internal repository references.
