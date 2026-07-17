---
name: clio-guidance-development
description: Develop, validate, restructure, or maintain reliable clio MCP guidance from live Creatio evidence. Use when researching an unfamiliar Creatio behavior, building a repeatable clio lab, turning runtime/source discoveries into agent-facing guidance, adding or revising GuidanceCatalog resources, eliminating duplicated guidance, or defining acceptance tests and ownership boundaries for a clio guidance family.
---

# Clio Guidance Development Framework

Develop clio guidance as a tested projection of observed behavior. Keep the research process repeatable, but make the published guidance usable without access to the original lab, debugger, database, or Creatio source tree.

## Keep two distinct artifacts

Maintain both artifacts throughout the work:

1. **Lab record:** preserve questions, chronology, commands, experiments, failures, debugger observations, logs, source findings, database checks, and unresolved cases.
2. **Projected guidance:** contain only the decisions, instructions, assertions, failure signals, recovery actions, and verified limitations an agent needs.

Do not publish a cleaned-up lab diary as guidance. Derive guidance from the evidence in the lab.

## Start with ownership and the live contract

1. Read the repository instructions and latest relevant workspace-diary entries.
2. Inventory current guidance before drafting anything:
   - `GuidanceCatalog`
   - `RoutingGuidanceResource`
   - related guidance resources
   - tool and prompt descriptions that route agents to guidance
   - unit and MCP end-to-end tests
3. Use the running clio MCP server as the tool-contract source of truth. Read `core-rules`, `routing`, the relevant guides, and each tool contract before invoking it.
4. Record the installed clio/Creatio versions when behavior may drift.
5. Create a concept-ownership table. Assign every semantic rule to exactly one canonical guide.

Routing owns guide names only. It must not repeat guide rules. Adjacent guides may require and link to a canonical owner, but must not reproduce its enum tables, operator catalogs, payload shapes, path grammar, or behavioral rules.

For ESQ filters, preserve one routed guidance family with one owner for each responsibility:

- `esq-filters`: route callers to the appropriate construction or parsing owner; do not repeat detailed rules.
- `esq-filters-frontend`: construct serialized JavaScript, page JSON, and DataService filters.
- `esq-filters-backend`: construct filters with Creatio's native backend C# `EntitySchemaQuery` API.
- `esq-filter-parsing`: consume the deserialized runtime filter tree and translate or evaluate it safely.

Keep construction and parsing separate even when one lab example exercises both. Each concrete rule must still have exactly one canonical owner.

Task-specific guides may describe when filtering occurs in their workflow, but must route filter construction and parsing details to those owners.

## Run the evidence loop

Repeat this loop for one observable question at a time.

### 1. Define the question and assertion

State one behavior to learn and the exact observation that will distinguish success from accidental success. Prefer a narrow question such as a binding name, runtime node shape, execution order, or one operator/value combination.

### 2. Build the smallest live fixture

Use a disposable or explicitly approved Creatio environment and a dedicated workspace/package. Create only the schemas, columns, code, records, and instrumentation needed for the question.

Use deterministic records with stable identifiers and deliberately distinct values. Keep the deploy/restart/query cycle attributable to one package. Do not introduce ClioGate unless the tested workflow actually requires it.

When one lab supports multiple executable examples, keep one stable platform adapter and delegate each example to a separate DI-resolved handler. Select the handler with a stable logical key stored in package-owned configuration, never with a CLR type name. Use an explicit registry, reject unknown or duplicate keys, and avoid reflection discovery or silent fallback. Bind both the configuration definition and its default value to the lab package so the selector is reproducible on installation.

Keep each example's deterministic mock data in the same file as its handler so a reviewer can understand and tailor the complete case locally. Prefer reviewable integration-test requests over one-off agent tool payloads for durable acceptance; for Creatio entity queries, use the clio integration-test scaffold and ATF.Repository models when that is the project-standard surface. Generate models into isolated staging, retain only the required reviewed graph, and record any provider adapter separately from the model/query behavior it preserves.

Assert repository model instances directly when model materialization is part of the behavior under test. Keep custom DTO or scalar projections only when projection is the explicit subject; when the repository supports it, prefer a model-shaped constructor projection so both column selection and model materialization remain observable. Make each fixture declare and select its own stable example key during setup, run fixtures non-parallel when selection changes shared environment state, and restore the package default during teardown. Give each independently selectable example the same stable scenario label across the server handler, test category, and reporting feature; keep external runners responsible only for environment and credential orchestration.

### 3. Instrument the decision boundary

Observe the runtime object at the point where an agent implementation must decide what to do.

- Use a debugger when the object graph is unknown or interactive inspection is fastest.
- Use structured, dedicated logging when observations must be retained or repeated.
- Convert useful debugger discoveries into stable diagnostic output or tests.

Capture runtime types, enum names, paths, flags, collection sizes, parameter CLR types, and sanitized values. Never log credentials, authorization headers, connection strings, or unrestricted business data.

### 4. Consult explanatory evidence

Use evidence in this order when practical:

1. live runtime behavior;
2. live clio MCP contract and guidance;
3. runtime inspection;
4. Creatio/clio source;
5. database or catalog inspection;
6. focused automated tests;
7. sample code.

Use source to explain observed behavior and find intentional guards. Do not make proprietary or machine-local source access a prerequisite of the finished guidance. Treat sample code as a hypothesis until runtime evidence confirms it.

### 5. Use a platform oracle

When reimplementing platform semantics, find an independent representation of Creatio's own interpretation. Examples include generated SQL, serialized metadata, designer-created artifacts, or a known platform endpoint.

Keep the oracle diagnostic. Distinguish compiling or interpreting a request from executing it and returning the correct result.

### 6. Implement only verified behavior

Add the smallest supported case. Fail closed on unsupported shapes whenever permissive fallback could broaden results, omit a filter, corrupt metadata, or conceal deployment failure.

Keep diagnostic seams separate from production behavior so the core translator or evaluator remains focused and testable.

### 7. Triangulate acceptance

Require independent signals when available:

```text
input request
  -> runtime object observation
  -> platform oracle representation
  -> implementation result
  -> exact expected records or effects
```

Run the final acceptance through the same clio MCP surface the guidance tells agents to use. Add focused automated tests for the durable behavior.

### 8. Preserve failures and boundaries

Append failures to the lab record with the exact input, error, last observable step, root cause, and resulting guidance decision. Do not erase a superseded interpretation; append the evidence that corrected it.

Mark every investigated case as one of:

- verified and executable;
- provisional;
- explicitly unsupported;
- still unknown.

Do not infer untested symmetry. Verifying one comparison, value type, nesting form, or provider does not verify its siblings.

## Project evidence into guidance

Convert every non-obvious finding through this chain:

```text
observation
  -> durable fact
  -> agent decision
  -> imperative instruction
  -> success assertion
  -> failure signal and recovery
```

Write for an agent performing the task, not for the researcher who discovered the behavior. Remove machine paths, environment names, timestamps, personal setup, and investigative narration unless expressed as parameterized examples.

Each guidance article should contain only what its concept owns and should make these items easy to find:

- scope and prerequisites;
- safe execution sequence;
- exact payload or code pattern where needed;
- observable success assertion;
- common failure signals and recovery;
- security, destructive-action, concurrency, and logging constraints;
- verified limitations and unsupported cases;
- links to canonical upstream guidance.

## Integrate guidance into clio

When publishing or restructuring MCP guidance:

1. Update the canonical guidance resource first.
2. Register or rename it in `GuidanceCatalog` when needed.
3. Update `RoutingGuidanceResource` with names only.
4. Update relevant tool and prompt descriptions so the guide is discoverable at the decision point.
5. Replace duplicated neighboring content with a mandatory cross-link.
6. Add contract tests for registration, stable URI/name, routing, mandatory cross-links, terminology, and critical invariants.
7. Add or update clio MCP end-to-end coverage when the MCP surface or executable guidance path changes.
8. Follow the repository's MCP implementation, testing, documentation, and review policies.

Prefer a shared executable fixture that drives authoring validation, runtime observation, provider translation, and final expected-result assertions. Avoid tests that prove only that a resource can be returned.

## Completion gate

Consider a guidance increment ready only when:

- at least one deterministic end-to-end example succeeds through clio MCP;
- runtime behavior and the platform oracle agree;
- focused tests preserve the established behavior;
- wrong paths have actionable failure signals and recovery;
- one canonical guide owns each concept;
- dependent guides route to owners without duplicating their content;
- the article works without access to local source or lab infrastructure;
- commands and payloads match the installed tool contract;
- sensitive logging and destructive/security implications are explicit;
- the lab record links the evidence behind every non-obvious rule;
- the verification boundary is stated honestly.

Append a concise workspace-diary entry after meaningful research or implementation. Re-run acceptance fixtures when the Creatio version, clio MCP surface, runtime object model, payload contract, or provider changes.
