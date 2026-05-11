# create-entity-business-rule

## Purpose

This capability defines behavior specific to creating entity-level business rules.

## Behavior

Entity-level rule creation must:

- create a new editable rule for the selected entity
- conditions
   - support left and right expression types:
      - attribute
      - constant
   - support data value types for attributes and constants:
      - text
      - number
      - boolean
      - GUID
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
      - relational operators only support numeric and temporal left attributes
      - temporal relational scope includes `Date`, `DateTime`, and `Time`
      - temporal constants must be sent as JSON strings and are normalized to typed metadata values before persistence
      - `Date` constants use `yyyy-MM-dd`
      - `DateTime` constants use ISO 8601 date-time with a required timezone suffix (`Z` or `±HH:mm`)
      - `Time` constants use ISO 8601 time with a required timezone suffix (`Z` or `±HH:mm`)
   - support multiple conditions 
   - support grouping conditions by AND or OR (without nested groups)
- condition
   - optional: omitting condition makes the rule always apply (useful for unconditional apply-static-filter)
- actions
   - support action types:
      - make readonly
      - make editable
      - make required
      - make optional
      - set values
      - apply-static-filter
   - support multiple actions
   - support set value targets:
      - constant value (text, number, boolean, date/time)
   - support multiple targets per action
   - apply-static-filter action:
      - requires `targetAttribute` (Lookup column name on the entity)
      - requires `filter` (friendly group with `logicalOperation`, `filters[]` leaves, optional `backwardReferenceFilters[]`)
      - must not include `items`
      - filter leaves support comparisonType tokens: EQUAL, NOT_EQUAL, GREATER, GREATER_OR_EQUAL, LESS, LESS_OR_EQUAL, IS_NULL, IS_NOT_NULL, START_WITH, NOT_START_WITH, CONTAIN, NOT_CONTAIN, END_WITH, NOT_END_WITH
      - `rootSchemaName` is inferred from the targetAttribute's reference schema; do not pass it
- generate internal identifiers automatically
- append the new rule without changing the order of existing rules

## Validation

Entity-level rule creation must reject the request when:

- entity does not exist
- caption is missing or empty
- the condition group is empty
- an action has no targets
- the request uses an unsupported action type
- the request uses an unsupported condition shape
- a unary comparison includes `rightExpression`
- a binary comparison omits `rightExpression`
- a relational comparison targets a non-numeric and non-temporal left attribute
- a referenced condition attribute does not exist in the target entity scope
- a referenced action target does not exist in the target entity scope
- an apply-static-filter action includes `items`
- an apply-static-filter action omits `targetAttribute`
- an apply-static-filter action omits `filter`
- an apply-static-filter action's filter is rejected by the server-side ESQ converter (e.g. unknown column path, datatype mismatch, missing lookup record, malformed backward reference)
- persistence fails
