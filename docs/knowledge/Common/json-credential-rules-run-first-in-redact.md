---
description: SensitiveErrorTextRedactor's JSON credential rule covers three quote spellings including the " one System.Text.Json emits, must run FIRST in Redact before BearerTokenRegex and CredentialPairRegex whose bare value class eats the value's closing backslash-quote escape, and must not carry the connection-string key uid because Creatio spells its identifier property UId
applies-to:
  - clio/Common/SensitiveErrorTextRedactor.cs
ticket: GH-1497
date: 2026-09-12
---

**What is true** - `Redact` scrubs a credential written as a JSON property, `"password":"x"`, which
`CredentialPairRegex` cannot reach at all: that rule needs `\b(key)\b\s*[=:]`, and the key's own closing
quote sits between the key and the colon. Three quote spellings are covered by ONE rule, the spelling
captured as `<q>` and written back unchanged: a literal `"`, the hand-written `\"`, and `"`. The
last one is not exotic - it is what `System.Text.Json`'s default encoder emits, measured:
`JsonSerializer.Serialize(new { body = "{\"password\":\"s3cr3t\"}" })` produces it, so a rule that knew
only the backslash-quote spelling would redact nothing on a real tool envelope.

**Why the rule runs first** - the issue text said "before `CredentialPairRegex`", and that is not
enough. Both later rules take the bare value class `[^\s,;"']+`, which stops at a quote but takes the
BACKSLASH before it, so on the backslash-quote spelling they eat the value's closing escape:
`\"authorization\":\"Bearer abc\"` loses its `\` to `BearerTokenRegex`, and `\"cookie\":\"BPMCSRF=abc\"`
loses it to `CredentialPairRegex`. A lone backslash is not a valid JSON escape. Measured, not reasoned:
moving the JSON rule after either of them turns both cases of
`Redact_ShouldKeepEscapedJsonParseable_WhenTheValueAlsoMatchesALaterRule` red. Nothing is lost by going
first - a secret-keyed value is replaced wholesale either way, so the URI/host/path rules have nothing
left to find inside it.

**Why the JSON key set is not the pair rule's key set** - the key words live in two constants.
`CredentialSecretKeys` is a secret in any spelling. `ConnectionStringPartKeys` is only a secret when it
is written as part of a connection string, and the JSON rule takes only `server`, `host`, `hostname` and
`database` from it. `uid` is deliberately excluded: it is also how Creatio spells the identifier property
`UId` on every schema, package and descriptor payload, so a JSON rule carrying it would replace an
ordinary identifier with a credential placeholder on practically every diagnostic body. `user id`,
`data source` and `initial catalog` are excluded with it - they do not occur as JSON property names in
this product. Do not "fix" the apparent drift by pasting the two sets back together.

**Accepted limits** - the rule is a last line of defence, not the only one, and the review on PR #1504
found no production path that writes a credential into an error message as a JSON property (the
reproductions behind the earlier, larger version of this rule were helper-level and synthetic). So the
following are knowingly NOT handled, and in each case the property is left exactly as found, so the
document still parses: an object or array value (a regex cannot balance brackets); a value sliced by an
input cap before its closing quote arrived; mixed quote spellings inside one document (the closing quote
is a backreference to the opening one, and `System.Text.Json` emits one spelling consistently, so a mixed
document is hand-written, not produced); an inner escaped quote inside an ESCAPED-level value, which ends
the match early and leaves the tail unredacted (`System.Text.Json` writes that inner quote as `\\` plus
the spelling, and the rule's escape unit consumes only the first two backslashes); and a doubly-escaped
document, an envelope serialized inside an envelope.

**What breaks if you ignore it** - the caller does not lose one field, it loses the whole response.
`ClioRunTool.RedactFailureContent` redacts a text block whose entire body is the tool's JSON envelope; a
document with a dangling backslash or an unbalanced quote stops parsing, so an agent that asked why a
deploy failed gets a parse error instead of the diagnostic. The CLI channel is redacted too (since
GH-1333 `Redact` is called from `ClassifyingDataProvider`, `SysSettingsManager` and
`ExceptionReadableMessageExtension`), but it only PRINTS the text and never parses it, so there the same
corruption is cosmetic - which is why the defect is easy to miss.
