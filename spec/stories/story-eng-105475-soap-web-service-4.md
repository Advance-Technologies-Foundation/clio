# Story 4: Nested test values

**Feature**: eng-105475-soap-web-service
**Jira**: ENG-105475 Register a REST web service through the AI Toolkit (Story; epic ENG-92704 Create BP via AI Toolkit)
**Spec**: [spec-eng-105475-soap-web-service.md](../prd/spec-eng-105475-soap-web-service.md) - R9
**ADR**: [adr-eng-105475-soap-web-service.md](../adr/adr-eng-105475-soap-web-service.md) - D10
**Test plan**: [tp-eng-105475-soap-web-service.md](../test-plans/tp-eng-105475-soap-web-service.md) - TC-U-D*, TC-I-D*
**Status**: ready-for-dev
**Size**: S
**Depends on**: story 1
**Blocks**: nothing

---

## As a / I want / So that

As a coding agent, I want to test-call a method whose parameters are arrays or object arrays, so that I can check a
real request without a process.

## Acceptance criteria

1. `parameters` accepts an array for an array parameter and an array of objects for an object array, sent through the
   platform's `nested` member (D10); a single object is refused.
2. Masking and the unknown-code refusal cover nested codes.
