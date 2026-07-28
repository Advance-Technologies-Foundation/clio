# ADR: project verified primitive comparisons into canonical guidance owners

## Decision

Publish native construction recipes only in `esq-filters-backend` and publish runtime validation/evaluation rules
only in `esq-filter-parsing`.

Use Integer to prove ordered scalar operators and MediumText to prove equality and positive/negative pattern
operators. Do not create an operator/type Cartesian product.

Record ATF's observed `A && B && C` structural order (`C, A, B`) as a versioned parity-test boundary. Semantic
parsers must not interpret child order as precedence.

## Consequences

- Agents receive concrete C# examples without duplicating frontend JSON guidance.
- Negative string predicates are taught as dedicated comparison leaves, not guessed leaf/group negation.
- Case-variant results are scoped to the virtual provider's explicit comparison policy and are not presented as
  Creatio/PostgreSQL collation evidence.
