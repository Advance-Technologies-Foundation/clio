# Story: publish the clio guidance development framework

- **Feature**: clio-guidance-development
- **Status**: review
- **Size**: M
- **SPEC**: [clio-guidance-development-spec.md](clio-guidance-development-spec.md)
- **ADR**: [clio-guidance-development-adr.md](clio-guidance-development-adr.md)
- **Test plan**: [clio-guidance-development-test-plan.md](clio-guidance-development-test-plan.md)

## As an agent guidance maintainer

I want a reusable evidence-to-guidance workflow plus canonical ESQ filter and virtual-entity articles,

so that unfamiliar Creatio behavior can be learned once, verified, and published without duplicated or speculative instructions.

## Acceptance criteria

- [x] One canonical `.ai` skill is reachable from portable Claude and Codex redirect skills.
- [x] ESQ filter construction and parsing have responsibility-specific canonical owners and stable routing.
- [x] Backend examples cover only lab-verified equality, numeric comparisons, AND/OR envelopes, and mixed nesting.
- [x] Runtime parsing is recursive, bounded, fail-closed, and explicit about unverified disabled/negated shapes.
- [x] Virtual entity guidance requires the object before `IEntityQueryExecutor` implementation.
- [x] Provider reads/writes require equivalent caller and tenant authorization plus bounded pushdown.
- [x] Virtual writes are gated to Creatio 10.0+, `EnableVirtualEntitySupport`, and an executable feature-enable fallback.
- [x] Catalog, routing, tool descriptions, unit tests, MCP E2E tests, and ClioRing compatibility checks pass.
