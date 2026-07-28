# Story: complete primitive Compare guidance

- **Feature**: esq-primitive-comparisons
- **Status**: review
- **Size**: S
- **Specification**: [esq-primitive-comparisons-spec.md](esq-primitive-comparisons-spec.md)
- **ADR**: [esq-primitive-comparisons-adr.md](esq-primitive-comparisons-adr.md)
- **Test plan**: [esq-primitive-comparisons-test-plan.md](esq-primitive-comparisons-test-plan.md)

## Acceptance criteria

- [x] Backend construction guidance contains concrete recipes for all verified scalar Compare operators.
- [x] Runtime parsing guidance validates column/parameter shape and dispatches dedicated negative text operators.
- [x] The `C, A, B` ATF parity boundary and provider-owned case-sensitivity policy are explicit.
- [x] Remaining unverified filter/value families stay visible.
- [x] Targeted MCP unit and E2E tests pass.
- [x] Comprehensive pre-PR review has no blocking findings.
