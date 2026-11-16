---
description: Studio's WSDL wizard lists an element of an anonymous type nested inside another element a second time one level too high (Fault/faultNumber beside Fault/detail/faultNumber); read-wsdl keeps the copy in the parser for parity and leaves it out of the arguments
applies-to:
  - clio/Command/WebServiceDesigner/WsdlParser.cs
  - clio/Command/WebServiceDesigner/WsdlRegistrationMapper.cs
ticket: ENG-105475
date: 2026-11-12
---

**What is true** — the wizard collects a complex type's fields with `querySelectorAll('sequence > element')` (or
`complexType > sequence > element` for an inline type) on the type node, and a CSS selector matches at any depth. The
fields of an anonymous type nested inside one of the fields therefore come back as fields of the outer type too, so
`AuthHeader/Scope/Tenant` also appears as `AuthHeader/Tenant`. Studio's own golden test for the CheckBlacklistStatus
WSDL expects such a copy (`tem:Fault/tem:faultNumber`). Measured by running the wizard's parser under jsdom on its
fixtures and on clio's.

**Why it is this way** — the selectors were written for named types whose fields sit directly in their sequence, and
the wizard's test for nested inline types checked only the item level.

**What breaks if you ignore it** — "fixing" the parser to read each element at its own level breaks parity with the
wizard's own expected output (AC3); passing the copy on registers a parameter for an element that does not exist there,
which a request sends as an extra element a strict server refuses. `WsdlParser` marks the copy (`IsCopy`) and
`WsdlRegistrationMapper` leaves it out with a warning.
