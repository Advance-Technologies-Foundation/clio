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
   - currently implemented:
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
- actions
   - support action types:
      - make readonly
      - make editable
      - make required
      - make optional
   - support multiple actions
   - support multiple targets per action
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
- persistence fails
