# object-business-rule-create

## Purpose

This capability defines behavior specific to creating object-level business rules.

## Behavior

Object-level rule creation must:

- create a new editable rule for the selected object
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

Object-level rule creation must reject the request when:

- object does not exist
- caption is missing or empty
- the condition group is empty
- an action has no targets
- the request uses an unsupported action type
- the request uses an unsupported condition shape
- a referenced condition attribute does not exist in the target object scope
- a referenced action target does not exist in the target object scope
- persistence fails
