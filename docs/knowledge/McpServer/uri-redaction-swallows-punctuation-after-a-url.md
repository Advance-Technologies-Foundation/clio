---
description: URI redaction preserves closing prose parentheses but still consumes other adjacent punctuation; userinfo must be matched separately to prevent partial credential leaks
applies-to:
  - clio/Common/SensitiveErrorTextRedactor.cs
  - clio/Package/ServiceResponseJsonGuard.cs
ticket: GH-1536
date: 2026-09-16
---

**What is true** — URI redaction excludes closing parentheses from the URI tail so
`(URL: https://host/route).` retains its closing punctuation. Other adjacent
punctuation, such as a full stop without a preceding parenthesis, can still be
consumed. The userinfo prefix separately accepts RFC 3986 punctuation through
`@`, including the complete Unicode escapes System.Text.Json emits for `&`,
apostrophe and `+`. Quote escapes remain outside the match.

**Why it is this way** — parentheses and apostrophes can delimit prose but are
also legal inside URI credentials. Applying the tail's delimiter rule to
userinfo leaves a partial password outside the match. Accepting every backslash
escape instead would risk swallowing the JSON delimiter around the URI.

**What breaks if you ignore it** — narrowing userinfo to the tail's character set
leaks credential fragments; widening the whole tail consumes closing prose or
JSON delimiters. Prefer a space after a diagnostic URI when the next punctuation
must be preserved, and keep userinfo and surrounding JSON escapes distinct.
