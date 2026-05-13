# ENG-88580: Read Entity Business Rules via MCP

## Summary

Add read-only business-rule retrieval to clio so a coding agent can inspect existing entity/object business rules before edit or delete flows. The implementation should expose two MCP tools: one to list rules for an entity and one to fetch a single rule by its generated business-rule name. The read path must use current Creatio stored metadata, preserve stored rule order for list results, and return a normalized agent-friendly contract rather than raw add-on JSON.

The task also requires updating the business-rules spec so the documented feature scope matches the new read capability and becomes the authoritative reference for the MCP contract.

## Implementation Changes

### Public MCP surface

- Add `list-entity-business-rules` as a read-only MCP tool.
  - Required args: `environment-name`, `package-name`, `entity-schema-name`.
  - Output: flat array of summaries in stored metadata order.
  - Each summary contains only `name`, `caption`, and `enabled`.
- Add `get-entity-business-rule` as a read-only MCP tool.
  - Required args: `environment-name`, `package-name`, `entity-schema-name`, `rule-name`.
  - `rule-name` matches the stored business-rule `name` exactly, case-insensitively.
  - Output: normalized object with `name`, `caption`, `enabled`, `condition`, and `actions`.
  - Reuse the existing nested `condition` and `actions` contract shape used by `create-entity-business-rule`.
- Update `get-tool-contract` so both tools are discoverable with accurate args, examples, read-only flags, and error contracts.

### Business-rule read path

- Refactor the current business-rule service flow so create/list/get share the same entity schema resolution and add-on metadata loading path.
- Reuse the current package + entity schema lookup rules; do not add package auto-resolution.
- Read current business-rule metadata from `AddonSchemaDesignerService.svc` and parse captions from resources instead of reconstructing values.
- Add a read-side mapper:
  - list mode maps directly from stored metadata/resources and does not require full rule normalization.
  - get mode converts one stored rule into the normalized agent contract.
- Keep `create-entity-business-rule` behavior unchanged.

### Normalization and failure rules

- Include disabled rules in both tools; expose the stored `enabled` value.
- Preserve stored business-rule order in list results.
- `get-entity-business-rule` must fail with a clear message when:
  - the target entity cannot be resolved,
  - the rule name is not found,
  - the stored rule shape cannot be mapped into the normalized contract.
- Do not return raw or partial metadata for unsupported shapes.
- `list-entity-business-rules` should still return `name`/`caption`/`enabled` summaries even when some rules have unsupported full shapes.

### Spec and policy-aligned updates

- Update `spec/business-rules/business-rules-spec.md` to include read support as a first-class operation, with explicit coverage for list and single-rule retrieval.
- Add or update a dedicated capability spec for the read flow so the contract, scope limits, failure behavior, and examples are documented in one place.
- Refresh `spec/business-rules/business-rules-architecture.md` to describe the shared read/write metadata flow and the normalized read mapping.
- Review command docs and skill references per repo policy; if they are unaffected, record that review outcome explicitly rather than changing them unnecessarily.

## Test Plan

- Unit tests for business-rule service and mapping:
  - list returns summaries with `name`, `caption`, and `enabled`.
  - list preserves stored metadata order.
  - get resolves by exact case-insensitive `rule-name`.
  - get returns normalized `name`/`caption`/`enabled`/`condition`/`actions`.
  - disabled rules are included.
  - missing rule returns a clear not-found error.
  - unsupported stored shapes fail in `get` but do not break `list`.
  - missing/invalid entity or package returns clear validation/runtime errors.
- MCP unit tests:
  - both tools expose the expected argument names and read-only metadata.
  - `get-tool-contract` advertises the two new tools and their output contracts correctly.
- MCP E2E tests:
  - create one or more rules, then verify list summaries and stored order.
  - verify get returns the normalized rule for a created rule name.
  - cover unsupported-shape behavior at unit level if seeding such metadata is impractical in E2E.
- Spec verification:
  - confirm examples, supported scope, and failure semantics in the spec match the implemented MCP behavior.
- Local validation:
  - `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&(Module=Command|Module=McpServer)" --no-build`
  - `dotnet test clio.mcp.e2e/clio.mcp.e2e.csproj --filter "FullyQualifiedName~EntityBusinessRuleToolE2ETests" --no-build`

## Important Interface Changes

- New MCP tool: `list-entity-business-rules`
- New MCP tool: `get-entity-business-rule`
- New list summary shape:
  - `name: string`
  - `caption: string`
  - `enabled: bool`
- New get shape:
  - `name: string`
  - `caption: string`
  - `enabled: bool`
  - `condition: BusinessRuleConditionGroup`
  - `actions: BusinessRuleAction[]`

## Assumptions and Defaults

- Scope is limited to entity/object business rules only; page-level rules remain out of scope.
- Both read tools require `package-name` and `entity-schema-name`.
- Business-rule `name` is assumed unique within the target entity.
- `get-entity-business-rule` uses exact case-insensitive name matching.
- The normalized read contract is intentionally limited to shapes the tool can map safely; unsupported shapes fail rather than degrade into raw JSON.
- `list-entity-business-rules` is a lightweight discovery tool and does not attempt full normalization for every rule.
