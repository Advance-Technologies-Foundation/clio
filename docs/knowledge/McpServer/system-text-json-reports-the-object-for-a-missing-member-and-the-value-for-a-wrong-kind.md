---
description: a binding JsonException's Path points at the OBJECT that lacks a required member ($, $.filters.all[0]) but at the VALUE of a wrong JSON kind ($.filters.all, $.pages[0].resources); the argument-shape diagnosis depends on that difference and stays silent when the path cannot be followed
applies-to:
  - clio/Command/McpServer/McpToolErrorFilter.ShapeDiagnosis.cs
date: 2026-11-11
---

**What is true** — when System.Text.Json refuses an MCP argument, `JsonException.Path` is relative to
the argument and names a different node for the two common failures. A missing `required` member
reports the path of the object that lacks it (`$` for the args record itself, `$.filters.all[0]` for a
filter condition) with the message "was missing required properties including: '<name>'". A value of
the wrong JSON kind reports the path of that value (`$.filters.all` when an object came where an array
belongs, `$.pages[0].resources` when an object came for a string). A property name with special
characters is written `['name']`. `McpToolErrorFilter.ShapeDiagnosis.cs` walks the caller's JSON and
the declared type down that path and reads the node: a kind mismatch there, or an object missing a
required member. It never parses the serializer's message text.

**Why it is this way** — the serializer checks required members when it closes the object, and the kind
when it reads the value; the path is where it stood at that moment.

**What breaks if you ignore it** — treating the path as "the member that is wrong" for every failure
names the parent object of a missing member as if its own kind were wrong, which is exactly the old
"argument 'args' must be an object" for a payload that was an object. And the walk explains only what
it can follow: a converter-bound member, a polymorphic type (its members depend on the discriminator),
a path segment it cannot resolve, or an object with no missing member returns no finding, and the
generic "contains a value that does not match the documented shape" text is kept. A serializer change
that moved these paths would therefore make messages less specific, not wrong - the unit tests in
`McpToolErrorFilterShapeDiagnosisTests` use the real serializer and fail on that.
