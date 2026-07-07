# business-rules

## Summary

The `business-rules` feature lets a coding agent work with Freedom UI business rules.

## Feature Areas

The feature is expected to scale across two dimensions:

### Scopes

- entity-level business rules
- page-level business rules

### Operations

- create rule
- read rule
- update rule
- delete rule

## Current Support

- create entity-level business rules - [create-entity-business-rules-spec.md](./create-entity-business-rules-spec.md)
- create page-level business rules - [create-page-business-rules-spec.md](./create-page-business-rules-spec.md)
- read / update / delete for both scopes - [business-rules-crud-spec.md](./business-rules-crud-spec.md)

## Batch creation

The `create-entity-business-rules` and `create-page-business-rules` MCP tools accept a `rules`
array and create every rule for the same entity/page schema in one call. The whole batch is applied
with a single add-on `SaveSchema`, one client-cache reset, and one configuration rebuild — instead of
that work repeating per rule — so creating N rules costs the same server round-trips as creating one.
A per-rule validation/conversion failure is isolated and reported in the per-rule result array; the
remaining rules are still saved. Callers should pass all rules for a schema in a single call rather
than calling the tool once per rule.

## Architecture

Implementation details, persistence flow, and internal metadata shape live in [business-rules-architecture.md](./business-rules-architecture.md).
