# Specification: ESQ primitive comparisons guidance

## Goal

Complete the backend ESQ Compare guidance with concrete, lab-verified C# construction and runtime parsing rules.

## Requirements

- Cover every scalar `FilterComparisonType` Compare operator with representative Integer or MediumText values.
- Keep native construction in `esq-filters-backend` and runtime interpretation in `esq-filter-parsing`.
- Record exact ATF structural behavior separately from logical evaluation semantics.
- Keep disabled/group-negation, Boolean/Guid, date/time, and non-Compare families explicitly unverified.
- Protect both articles through direct MCP resource and `get-guidance` tests.

## Evidence

The VirtualEntityGuidance lab compared native C# writers with ATF.Repository 2.0.3.5 requests on Creatio
10.1.298.0/.NET 8/PostgreSQL. Forty-eight package unit tests and six focused live tests passed.
