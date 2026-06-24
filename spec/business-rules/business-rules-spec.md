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
- edit rule
- delete rule


## Current Support

The current feature support is:

- create entity-level business rule - [create-entity-business-rule-spec.md](./create-entity-business-rule-spec.md)
- create page-level business rule - [create-page-business-rule-spec.md](./create-page-business-rule-spec.md)
- read entity-level business rules - [read-entity-business-rule-spec.md](./read-entity-business-rule-spec.md)

Read at page scope, and edit/delete at both scopes, remain planned.

## Architecture

Implementation details, persistence flow, and internal metadata shape live in [business-rules-architecture.md](./business-rules-architecture.md).
