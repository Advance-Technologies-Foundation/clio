# business-rules

## Summary

The `business-rules` feature lets a coding agent create, read, update, and delete Freedom UI
business rules for both **entity** and **page** scopes. Every operation is exposed as an MCP
tool only (no CLI verb).

## Tools

| Tool | Scope | Operation |
|---|---|---|
| `create-entity-business-rules` | entity | create rules on an entity schema |
| `create-page-business-rules` | page | create rules on a page schema |
| `read-entity-business-rules` | entity | read all rules for an entity schema |
| `read-page-business-rules` | page | read all rules for a page schema |
| `update-entity-business-rules` | entity | update rules matched by `name` |
| `update-page-business-rules` | page | update rules matched by `name` |
| `delete-entity-business-rules` | entity | delete rules by `name` |
| `delete-page-business-rules` | page | delete rules by `name` |

## Batch semantics

Create/update tools take a `rules` array; delete tools take a `rule-names` array. The whole
batch for one schema is applied with a single add-on `SaveSchema`, one client-cache reset, and
one configuration rebuild — so creating/updating/deleting N rules costs the same server
round-trips as one. A per-rule validation/conversion failure is isolated and reported in the
per-rule result array; the remaining rules are still saved. Always pass every rule for a schema
in a single call rather than calling the tool once per rule.

## Rule contract

The same rule contract is used across create, read output, and update input. Three fields are
optional at the contract level:

- `name` (rule level) — the unique internal rule identifier (e.g. `BusinessRule_1c48625`).
- `enabled` (rule level) — whether the rule is active. Defaults to `true`.
- `uId` (block level) — stable identity of conditions, expressions, actions, and set-value items.
  Preserving block uIds lets the platform store a short diff when a rule is overridden in another
  package, instead of a full copy.

Per-operation requirements:

| Field | create | read (output) | update (input) | delete |
|---|---|---|---|---|
| `name` | optional; honored when supplied (must be unique), otherwise generated | always returned | **required** (match key) | **required** (`rule-names` array) |
| `enabled` | optional, default `true` | always returned | optional; omitted → existing value preserved | n/a |
| block `uId` | optional; supplied → preserved, omitted → generated | always returned | optional; supplied → preserved, omitted → regenerated | n/a |

Example (update input / read output item):

```json
{
  "caption": "Readonly Name",
  "name": "BusinessRule_1c48625",
  "enabled": true,
  "condition": {
    "logicalOperation": "AND",
    "conditions": [
      {
        "uId": "b8f788e2-3530-41cc-8a0c-6a256f179fa2",
        "leftExpression": { "uId": "e277d35c-…", "type": "AttributeValue", "path": "Name" },
        "comparisonType": "equal",
        "rightExpression": { "uId": "81b8b8ea-…", "type": "Const", "value": "Readonly" }
      }
    ]
  },
  "actions": [
    { "uId": "c334b501-…", "type": "make-read-only", "items": ["Name"] }
  ]
}
```

## Create

Common to both scopes, rule creation must:

- create a new editable rule for the selected schema
- append the new rule without changing the order of existing rules
- support a single top-level condition group (AND or OR) without nested groups
- support multiple conditions and multiple actions
- generate internal identifiers automatically (see the "Decisions" note on caller `uId`s)
- generate trigger metadata from condition attributes without requiring callers to pass trigger
  or scope identifiers

### Entity scope

Entity-level rule creation additionally supports:

- conditions
   - left and right expression types on either side, in any pairing: attribute, constant, system variable
   - system variables on either side:
      - `CurrentDate` (Date), `CurrentTime` (Time), `CurrentDateTime` (DateTime)
      - `CurrentUser` (Lookup → `SysAdminUnit`), `CurrentUserContact` (Lookup → `Contact`), `CurrentUserAccount` (Lookup → `Account`), `CurrentUserRoles` (ObjectList of `SysAdminUnit` roles)
      - role-based logic: `CurrentUserRoles` `contain`/`not-contain` a constant `SysAdminUnit` role id
      - both operands must resolve to the same data value type (an `ObjectList` is compared element-wise to a `Lookup`); lookup operands must reference the same schema
      - a constant operand inherits its data value type and reference schema from the operand it is compared against
   - comparison types `contain`/`not-contain` for collection (`ObjectList`) and text operands
   - data value types for attributes and constants: text, number, boolean, GUID, date/time
   - comparison types: equal, not equal, is filled in, is not filled in, greater than, greater than or equal, less than, less than or equal
   - operand rules:
      - `is filled in` / `is not filled in` are unary and omit `rightExpression`; the binary comparisons require `rightExpression`
      - relational operators only support numeric and temporal left attributes (temporal scope: `Date`, `DateTime`, `Time`)
      - temporal constants are sent as JSON strings and normalized before persistence: `Date` → `yyyy-MM-dd`; `DateTime`/`Time` → ISO 8601 with a required timezone suffix (`Z` or `±HH:mm`)
- actions
   - action types: make readonly, make editable, make required, make optional, apply filter
   - set-value targets: constant value (text, number, boolean, date/time); formula value using a simple numeric direct-field arithmetic expression such as `(Field1 + Field2) / 2`; attribute value from a same-typed direct attribute or forward reference path such as `Lookup.Field`
   - dynamic lookup filtering (`apply-filter`):
      - one top-level `apply-filter` action per rule; target and source must be direct lookup attributes on the root entity
      - `targetFilterPath` resolves inside the target lookup schema; optional `sourceFilterPath` resolves inside the source lookup schema; final endpoints must resolve to the same data value type
      - empty outer condition groups are allowed only for `apply-filter`
      - persistence expands `apply-filter` into a parent filter rule plus autogenerated child clear/populate rules when requested
   - `apply-static-filter`: restricts a lookup to records matching a fixed condition (see `business-rule-filters` guidance)

Entity-level creation rejects the request when:

- entity does not exist; caption is missing or empty; the condition group is empty; an action has no targets
- the request uses an unsupported action type or an unsupported condition shape
- a unary comparison includes `rightExpression`, or a binary comparison omits it
- a relational comparison targets a non-numeric and non-temporal left attribute
- a right-side system variable name is unknown, its data value type does not match the left attribute, or a right-side lookup system variable references a different schema than the left lookup attribute
- a referenced condition attribute, action target, or set-value source does not exist in the target entity scope
- a set-value source and target have different data value types, or a set-value target uses a forward reference path
- an `apply-filter` target/source is not a direct lookup attribute; its lookup path does not exist; endpoints resolve to different data value types or reference schemas; the action is combined with any other entity action; or it sets `populateValue=true` together with `sourceFilterPath`
- a formula source attribute does not exist, does not reference any entity attribute, is not numeric, or uses a construct outside the arithmetic whitelist (function call, comparison operator, string literal, other expression shapes)
- persistence fails

### Page scope

Page-level rule creation targets page business-rule metadata stored as a `BusinessRule` add-on
for `ClientUnitSchemaManager`, resolves the page schema hierarchy, and validates against the
merged page bundle. It additionally supports:

- conditions
   - left/right expression types on either side, in any pairing: declared page attribute, constant, system variable
   - the same system variables as the entity scope, plus current-user visibility (the no-code alternative to a page handler): `CurrentUser`/`CurrentUserContact`/`CurrentUserAccount` `equal`/`not-equal` a constant id
   - additional condition operand sources are gated behind the runtime feature flag `page-business-rule-condition-sources` (OFF by default; enable with `clio experimental --name page-business-rule-condition-sources --enable`)
   - an `AttributeValue` operand carries an optional `scopeId` — the platform discriminator resolved at runtime by `Context.GetAttributeByPath(path, scopeId)`:
      - omitted / empty → a **root page attribute**: a surfaced datasource-bound attribute (declared in `bundle.viewModelConfig.attributes` with a `modelConfig.path`) **or** an unbound/technical page-local attribute (an attribute with a declared type / default value and no `modelConfig.path`)
      - `"PageParameters"` → a **page parameter** (`path` = parameter name)
      - `"<DataSource name>"` (a name from `bundle.modelConfig.dataSources`, e.g. `PDS`) → a **DataSource field**, including a column **not** surfaced on the page (`path` = column name; forward paths such as `Contact.Account` are allowed)
   - a new operand type `SysSetting` (field `sysSettingName`, the SysSettings code) compares against a system setting, inheriting the compared operand's data value type (like a `Const`) — this satisfies the "compare a page parameter to a system setting" AC
   - while the feature flag is **off**, page rules keep prior behaviour: only root surfaced datasource-bound attributes are allowed, referenced by declared page attribute name (e.g. `PDS_UsrText_r07ym9c`), **not** datasource path (e.g. `PDS.UsrText`); page attribute data value types are resolved from the datasource entity schema
   - caveat: a rule authored with these sources **executes** at runtime, but the legacy 7.x visual condition designer may not yet round-trip a page-parameter / scoped operand for manual editing (the DataSource-field scope is proven by shipped Creatio packages and is the safest); see [ENG-93262-sdd.md](./ENG-93262-sdd.md) §12 for the full spike findings
   - data value types: text, number, boolean, GUID, lookup GUID, date/time
   - the same comparison types and operand rules as the entity scope; right-side `AttributeValue` is supported when both attributes resolve to the same data value type; lookup constants are sent as GUID strings
- actions
   - action types: hide element, show element, make editable, make read-only, make required, make optional
   - any named page element collected recursively from `bundle.viewConfig`, referenced by element name (e.g. `Input_0dqt4ly`, `EscalateButton`)
   - validation checks only that each target element exists in the resolved recursive `viewConfig` — not designer group membership or component-specific support for the requested behavior
   - `apply-filter` / `apply-static-filter` are **not** supported on pages (use entity rules — they apply everywhere the lookup is used)

Page-level creation rejects the request when:

- the page schema or package does not exist; the schema hierarchy cannot be loaded; the page bundle cannot be built
- caption is missing or empty; the condition group is empty; the request uses nested condition groups or an unsupported logical operation or condition shape
- a unary comparison includes `rightExpression`, or a binary comparison omits it
- a relational comparison targets a non-numeric and non-temporal left attribute
- a left/right `AttributeValue` supplies a `.`-containing `path` without a `scopeId` — it still gets the "must use the declared page attribute name, not the datasource path" error; a referenced condition attribute is not declared in the page view model, not bound to a datasource column, or cannot be resolved to an entity schema column
- while the feature flag `page-business-rule-condition-sources` is off, any non-empty `scopeId` (page parameter or DataSource field) or `SysSetting` operand is rejected; entity rules always reject a non-empty `scopeId` and a `SysSetting` operand (single scope only)
- left/right attribute expressions resolve to different data value types, or a constant value does not match the resolved left attribute data value type
- a right-side system variable is unknown, mismatched in data value type, or references a different lookup schema than the resolved left attribute
- the request uses an unsupported page action type, an action has no target elements, or a target element does not exist in the merged recursive `viewConfig`
- persistence fails

## Read

- Input: `environment-name`, `package-name`, `entity-schema-name` / `page-schema-name`.
- Fetches the `BusinessRule` add-on schema with `useFullHierarchy=true` (the same request the
  create path uses), so inherited rules from parent packages are included.
- Autogenerated child rules (`parentUId` set — the clear/populate helpers of `apply-filter`) are
  **not** returned as separate rules; they are represented by the parent action's `clearValue` /
  `populateValue` flags.
- Every rule is returned directly in the contract shape (the update input shape) with `name`,
  `enabled`, `caption`, and block `uId`s — no wrapper object.
- A rule the contract cannot represent (multi-case, unknown action type, nested condition group)
  fails the whole read with an error naming the rule. The reader is deliberately permissive about
  the platform's metadata normalization (stripped `type` markers, stripped zero-valued properties,
  dropped apply-filter flags), so this is not expected for designer- or clio-authored rules.
- `apply-static-filter` rules are returned with the target attribute and a friendly `filter`
  object: the persisted ESQ envelope is decompiled back into the same friendly `filter` shape used
  on create (via `FullToSimpleFilterConverter`, the inverse of `SimpleToFullFilterConverter`). One
  lossy point — a Lookup filter value reads back as the stored display name (Id fallback), so a
  display-name-ambiguous lookup could re-resolve to a different Id on update. To change such a
  rule, the caller supplies a friendly `filter` again and the envelope is regenerated.
- Formula set-value items are returned best-effort with the persisted expression text.

## Update

- Input mirrors create; each rule additionally **requires** `name`.
- A rule is matched by `name` (case-insensitive) against the fetched add-on metadata. No match →
  per-rule failure; the remaining rules still save (same isolation model as batch create). Update
  is not an upsert.
- The matched rule is fully replaced by the converted new definition. The converter is given the
  existing rule node and stamps stable identity **during construction** (no post-conversion merge):
  - rule `uId` — always taken from the existing rule (caller never sends a rule uId);
  - case and top-level condition-group `uId` — taken from the existing rule;
  - trigger `uId`s — matched by (name, type) against existing triggers;
  - condition / expression / action / set-value-item `uId`s — caller-supplied values are preserved
    positionally; blocks without a caller uId get fresh ones.
- `enabled` omitted → the existing rule's value is preserved; supplied → applied.
- For `apply-filter` rules the autogenerated child rules of the matched rule are removed and
  regenerated to match the new definition, anchored to the existing rule uId from the start
  (`parentUId`, `Autogenerated_{uId}_…` names).
- The caption resource (`{ruleUId}.Caption`) is updated.

## Delete

- Input: `environment-name`, `package-name`, `entity-schema-name` / `page-schema-name`,
  `rule-names` (array of internal rule names).
- Each named rule is removed together with its autogenerated child rules (`parentUId` cascade) and
  its caption resources. Unknown name → per-name failure; remaining names still delete.
- Nothing is saved when no name matched.

## Package layering

`GetSchema(useFullHierarchy: true)` returns the merged rule set across the package hierarchy;
`SaveSchema` persists into the target package. Updating or deleting a rule that originates in a
parent package therefore stores a layered diff in the target package — this is why rule and block
uIds must be preserved.

## Decisions

- **Create honors caller `uId`s when supplied** (like update): a supplied block `uId` is preserved,
  a fresh one is generated only when omitted. Callers own uId uniqueness — the same trust update
  relies on — so there is no separate strip-on-create pass. Create also honors `enabled` and a
  caller-supplied unique `name`.
- **Read returns flat contract-shaped rules and fails loudly on an unrepresentable one** instead of
  wrapping every item in a `convertible`/`raw` envelope. No unrepresentable rule has been observed on
  a real environment, the reader tolerates all known platform normalization, and the envelope taxed
  the common case to serve a hypothetical one.
- **Update replaces, never merges.** Partial patching of a rule is out of scope; the short-diff
  optimization comes from uId preservation, not from patch semantics.
- **Delete cascades autogenerated children.** Orphaned clear/populate helper rules would otherwise
  survive with dangling `parentUId`.
- **No feature toggle** — the tools are public.

## Architecture

Implementation details, persistence flow, and internal metadata shape live in
[business-rules-architecture.md](./business-rules-architecture.md).
