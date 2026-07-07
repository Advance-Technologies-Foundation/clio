# create-page-business-rules

## Purpose

This capability defines behavior specific to creating Freedom UI page-level business rules.

## Behavior

Page-level rule creation must:

- create a new editable rule for the selected Freedom UI page schema
- target page business-rule metadata stored as a `BusinessRule` add-on for `ClientUnitSchemaManager`
- resolve the page schema hierarchy and validate against the merged page bundle
- preserve existing page business rules and append the new rule without changing the order of existing rules
- conditions
   - support left and right expression types on either side, in any pairing:
      - declared page attribute
      - constant
      - system variable
   - support system variables on either side of a condition:
      - `CurrentDate` (Date), `CurrentTime` (Time), `CurrentDateTime` (DateTime)
      - `CurrentUser` (Lookup → `SysAdminUnit`), `CurrentUserContact` (Lookup → `Contact`), `CurrentUserAccount` (Lookup → `Account`), `CurrentUserRoles` (ObjectList of `SysAdminUnit` roles)
      - role-based / current-user visibility (the no-code alternative to a page handler): `CurrentUserRoles` `contain`/`not-contain` a constant `SysAdminUnit` role id, or `CurrentUser`/`CurrentUserContact`/`CurrentUserAccount` `equal`/`not-equal` a constant id
      - both operands must resolve to the same data value type; lookup operands must reference the same schema
      - a constant operand inherits its data value type and reference schema from the operand it is compared against
   - support comparison types `contain` and `not-contain` for collection (`ObjectList`) and text operands
   - support condition attributes only when they are declared in `bundle.viewModelConfig.attributes` and bound to an entity datasource column through `modelConfig.path`
   - use declared page attribute names in payloads, for example `PDS_UsrText_r07ym9c`
   - do not use datasource paths in payloads, for example `PDS.UsrText`
   - resolve page attribute data value types from the datasource entity schema
   - support data value types for attributes and constants:
      - text
      - number
      - boolean
      - GUID
      - lookup GUID
      - date/time
   - support comparison types:
      - equal
      - not equal
      - is filled in
      - is not filled in
      - greater than
      - greater than or equal
      - less than
      - less than or equal
   - operand rules:
      - `is filled in` and `is not filled in` are unary and omit `rightExpression`
      - `equal`, `not equal`, `greater than`, `greater than or equal`, `less than`, and `less than or equal` require `rightExpression`
      - right-side `AttributeValue` expressions are supported when both attributes resolve to the same data value type
      - relational operators only support numeric and temporal left attributes
      - temporal relational scope includes `Date`, `DateTime`, and `Time`
      - lookup constants must be sent as GUID strings
      - temporal constants must be sent as JSON strings and are normalized to typed metadata values before persistence
      - `Date` constants use `yyyy-MM-dd`
      - `DateTime` constants use ISO 8601 date-time with a required timezone suffix (`Z` or `±HH:mm`)
      - `Time` constants use ISO 8601 time with a required timezone suffix (`Z` or `±HH:mm`)
   - support multiple conditions
   - support grouping conditions by AND or OR (without nested groups)
- actions
   - support action types:
      - hide element
      - show element
      - make editable
      - make read-only
      - make required
      - make optional
   - support any named page element collected recursively from `bundle.viewConfig`
   - use page element names in payloads, for example `Input_0dqt4ly` or `EscalateButton`
   - validate only that each target page element exists in the resolved recursive `viewConfig`
   - do not validate designer group membership or component-specific support for editability, read-only, required, or optional behavior
   - support multiple actions
   - support multiple page elements per action
- generate internal identifiers automatically
- generate trigger metadata from condition attributes without requiring callers to pass trigger identifiers or scope identifiers

## Validation

Page-level rule creation must reject the request when:

- page schema does not exist
- package does not exist
- page schema hierarchy cannot be loaded
- page bundle cannot be built
- caption is missing or empty
- the condition group is empty
- the request uses nested condition groups
- the request uses an unsupported logical operation
- the request uses an unsupported condition shape
- a unary comparison includes `rightExpression`
- a binary comparison omits `rightExpression`
- a relational comparison targets a non-numeric and non-temporal left attribute
- left or right `AttributeValue` uses a datasource path instead of a declared page attribute name
- a referenced condition attribute is not declared in the page view model
- a referenced condition attribute is not bound to an entity datasource column
- a referenced condition attribute cannot be resolved to an entity schema column
- left and right attribute expressions resolve to different data value types
- a constant value does not match the resolved left attribute data value type
- a right-side system variable name is unknown
- a right-side system variable data value type does not match the resolved left attribute data value type
- a right-side lookup system variable references a different schema than the resolved left lookup attribute
- the request uses an unsupported page action type
- an action has no target page elements
- a referenced action target does not exist as a named element in the merged recursive `viewConfig`
- persistence fails
