# ENG-88576 Implementation Plan for `create-entity-business-rule`

## Summary

Extend the existing `create-entity-business-rule` MCP tool with a new `set-values` action while keeping all current capabilities unchanged. The new action will support multiple assignments per action item, allow mixing with existing action types in the same rule, keep `dataValueTypeName` out of the MCP contract by deriving it during DTO conversion, support forward-reference attribute paths such as `Country.Capitol` or `Employee.Manager.Name` when used as assignment values, and include full lookup support for targets, source paths, and constants.

## Implementation Changes

- Refactor the business-rule action model from a single flat `BusinessRuleAction` shape into a discriminator-based action family.
  - Preserve current action payloads for `make-editable`, `make-read-only`, `make-required`, and `make-optional`.
  - Add a new `set-values` action payload:
    - `type: "set-values"`
    - `items: [{ expression: { type: "AttributeValue", path }, value: { type: "Const", value } | { type: "AttributeValue", path } }]`
- Update JSON binding so existing action payloads still deserialize unchanged and `set-values` deserializes into a dedicated action type.
- Extend schema-aware validation for the new action branch.
  - Require at least one `set-values.items[*]` entry.
  - Require `items[*].expression` to be a direct target attribute reference on the root entity.
  - Allow lookup columns as `items[*].expression` targets.
  - Allow `items[*].value` as either a constant or an attribute reference.
  - Support forward-reference dotted paths for `items[*].value.path` by walking lookup/reference columns segment by segment through related schemas.
  - Allow lookup segments as intermediate hops in a dotted source path.
  - Allow the terminal source segment to be either scalar or lookup, but enforce compatibility with the target column.
  - For lookup constants, require the MCP payload to use the same raw GUID-string convention already used for lookup condition constants.
  - Reject unresolved paths, non-reference intermediate segments, empty path segments, and incompatible target/source types with field-specific messages.
  - Keep existing validator behavior for pre-existing action types unchanged.
- Extend schema resolution helpers beyond the current exact-name column index.
  - Keep fast direct-column lookup for existing rules and set-values targets.
  - Add a relation-walk resolver for dotted value paths that uses `ReferenceSchema` metadata from the loaded entity design schema and loads related schemas as needed.
  - Return enough descriptor data to infer the terminal value type and reference schema for conversion and compatibility checks.
- Extend DTO conversion to emit core-compatible `BusinessRuleActionSetValues` metadata.
  - Derive `dataValueTypeName` and `referenceSchemaName` dynamically from resolved target/source descriptors.
  - Map each MCP `set-values.items[*]` entry into the persisted target-expression plus value-expression shape used by core.
  - Support all three assignment modes:
    - scalar target from scalar constant
    - scalar or lookup target from attribute path
    - lookup target from lookup GUID constant
  - Emit dotted source paths when they resolve successfully and pass compatibility checks.
  - Keep current conversion for existing action types unchanged.
- Update MCP contract surface for `create-entity-business-rule`.
  - Add the new action type to validators, descriptions, and examples in `ToolContractGetTool`.
  - Add examples for:
    - scalar constant assignment
    - dotted scalar source path such as `Employee.Manager.Name`
    - lookup target assignment from a lookup attribute path
    - lookup target assignment from a GUID-string `Const`
  - Document that lookup constants must be passed as raw GUID strings.
  - Keep the tool name, top-level parameters, and current output contract unchanged.
- Update the feature spec as part of implementation.
  - Refresh `spec/business-rules/create-entity-business-rule-spec.md` so it becomes the authoritative behavior spec for the expanded tool contract.
  - Update `spec/business-rules/business-rules-spec.md` if needed so the feature summary remains aligned with current support.
  - If `business-rules-architecture.md` references the old action model too narrowly, update the architecture notes to reflect the new `set-values` path, dotted source resolution, and lookup handling.
- Review repo policy surfaces tied to command/MCP/spec changes and update only if they are now inaccurate.
  - Command docs: verify whether command docs need changes; if no command-facing docs are impacted, record that docs were reviewed.
  - MCP artifacts: keep MCP prompt/resource/tool guidance aligned with the new contract.
  - Skill guidance: review required skill files and update only if the command/spec behavior change makes them stale.
- Append a concise workspace diary entry after implementation with the contract, dotted-path resolution, lookup support, and spec-update decisions.

## Public Interface Changes

- `rule.actions[*]` becomes a mixed action union instead of a single flat shape.
- Existing actions remain unchanged:
  - `{"type":"make-required","items":["Owner"]}`
- New action:
  ```json
  {
    "type": "set-values",
    "items": [
      {
        "expression": {
          "type": "AttributeValue",
          "path": "TargetTextColumn"
        },
        "value": {
          "type": "AttributeValue",
          "path": "Employee.Manager.Name"
        }
      },
      {
        "expression": {
          "type": "AttributeValue",
          "path": "Manager"
        },
        "value": {
          "type": "AttributeValue",
          "path": "Employee.Manager"
        }
      },
      {
        "expression": {
          "type": "AttributeValue",
          "path": "Manager"
        },
        "value": {
          "type": "Const",
          "value": "11111111-1111-1111-1111-111111111111"
        }
      }
    ]
  }
  ```
- `dataValueTypeName` stays internal and is not exposed on the MCP contract.
- Forward-reference dotted paths are supported only for `set-values.items[*].value.path`, not for existing action targets and not for condition operands in this ticket.
- Lookup constants are supported for `set-values.items[*].value` and use raw GUID strings.

## Test Plan

- Add or update unit coverage for MCP payload binding in `BusinessRuleToolTests`.
  - Existing flat actions still deserialize and map correctly.
  - `set-values` with scalar constants deserializes correctly.
  - `set-values` with direct scalar attribute-source values deserializes correctly.
  - `set-values` with dotted scalar source values such as `Employee.Manager.Name` deserializes correctly.
  - `set-values` with lookup attribute-source values deserializes correctly.
  - `set-values` with lookup GUID constants deserializes correctly.
  - Mixed `actions[]` payloads deserialize correctly.
- Add or update unit coverage for validation and conversion in the business-rule command/service tests.
  - Reject missing `set-values.items`.
  - Reject non-attribute targets.
  - Reject unresolved target/source paths.
  - Reject dotted paths with invalid intermediate reference segments.
  - Reject lookup constants that are not GUID strings.
  - Reject incompatible target/source combinations.
  - Verify inferred DTO metadata for:
    - scalar constant assignments
    - direct attribute assignments
    - dotted scalar-source assignments
    - lookup-target from lookup-path assignments
    - lookup-target from GUID-string constant assignments
  - Verify existing action types still convert unchanged.
- Update `ToolContractGetToolTests` so the published contract advertises `set-values`, shows the nested action shape, includes lookup and dotted-path examples, and documents raw GUID-string lookup constants while preserving current canonical flow/output behavior.
- Extend `EntityBusinessRuleToolE2ETests` with a real `set-values` creation scenario that verifies persisted add-on metadata contains the new action shape; if the sandbox entity lacks safe lookup and dotted-path examples, cover those combinations at unit level and keep E2E on the simplest supported combinations.
- Validate the spec updates alongside code changes so examples, supported behavior, and restrictions match the implemented contract.
- Local validation commands during implementation:
  - `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&(Module=Command|Module=McpServer)" --no-build`
  - `dotnet test clio.mcp.e2e/clio.mcp.e2e.csproj --filter "FullyQualifiedName~EntityBusinessRuleToolE2ETests" --no-build`

## Assumptions and Defaults

- The single existing MCP tool `create-entity-business-rule` remains the only entry point.
- Existing capabilities are preserved exactly; ticket-specific restrictions apply only to the new `set-values` branch.
- A rule may mix `set-values` with existing action types in one `actions[]` array.
- `set-values` supports multiple assignments in one action.
- `set-values.items[*].value` supports only `Const` and `AttributeValue` in this delivery.
- Forward-reference dotted paths are supported for assignment values only.
- Lookup targets, lookup-valued source paths, and lookup GUID constants are all in scope for this implementation.
- Lookup constants on the MCP surface use raw GUID strings, consistent with existing lookup-condition handling.
- `dataValueTypeName` and related DTO metadata are inferred from schema resolution and are not accepted from MCP callers.
