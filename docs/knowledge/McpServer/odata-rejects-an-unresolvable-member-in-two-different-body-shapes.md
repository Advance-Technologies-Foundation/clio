---
description: Creatio answers an unresolvable OData member with two different bodies - a raw foreign-key column gives "An error has occurred." with the cause two levels down in innererror/internalexception, an unknown property names itself in the headline - so a classifier reading only error.message sees an unexplained server error
applies-to:
  - clio/Common/CreatioResponseError.cs
ticket: GH-1407
date: 2026-09-12
---

**What is true** — measured on a real .NET Framework stand (Creatio 10.2.117, PostgreSQL), a rejected
`$filter` comes back with HTTP 200 and one of two shapes:

1. **A raw foreign-key column** (`$filter=SysSettingsId eq <guid>` on `SysSettingsValue`):

   ```json
   {"error":{"code":"","message":"An error has occurred.",
     "innererror":{"message":"The 'ObjectContent`1' type failed to serialize the response body ...",
       "internalexception":{"message":"Column by path SysSettingsId not found in schema SysSettingsValue."}}}}
   ```

   The headline says nothing; the only useful sentence is two levels down, and the second level is
   named `internalexception`, not `innererror`.

2. **An unknown property** (`$filter=Name eq 'x'` on the same entity):

   ```json
   {"error":{"code":"","message":"The query specified in the URI is not valid. Could not find a property named 'Name' on type 'Terrasoft.Configuration.OData.SysSettingsValue'.",
     "innererror":{"message":"Could not find a property named 'Name' ..."}}}
   ```

Filtering the same entity through the navigation path (`SysSettings/Id`) succeeds, which is what makes
shape 1 a query-shape failure and not a platform fault.

`CreatioResponseError.TryClassify` therefore walks the `innererror` / `internalexception` chain
(lower-case only — `TryGetProperty` is case-sensitive and no measured body uses another casing) and
matches three wordings: `The query specified in the URI is not valid`, `Could not find a property
named`, and `not found in schema`. A looser second rule — "the text names a `$filter`/`$orderby`/… and
contains a failure word" — was written and removed: a server message that merely echoes the request
URI (an expired-session body, say) then classifies as `invalid-query` and sends the agent to correct
field names that were never wrong.

The same wordings also appear while the OData model is REBUILDING after a column or entity was just
added, which is why the caller-facing message says to wait and retry once before changing anything.

**Why it is this way** — shape 1 is a serialization failure raised while the response is being written,
so the ASP.NET pipeline reports its own top-level message and nests the original; shape 2 is rejected
by the query parser before any serialization happens.

**What breaks if you ignore it** — classifying on `error.message` alone puts shape 1 into
`server-reported-error`, which tells the caller to go and read the environment's server logs for what
is in fact a one-character fix in their own filter. That is exactly the failure issue #1407 reports as
"a bare, detail-free string".
