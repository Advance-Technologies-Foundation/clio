---
description: FontFamilyName.Validate is the only font-family validation in clio, so ApplyFonts must call it for every family even when NeedsImport suppresses the @import, and the grammar accepts a trailing space - only the family == family.Trim() check rejects padding
applies-to:
  - clio/Theming/FontFamilyName.cs
  - clio/Theming/ThemeCssBuilder.cs
ticket: ENG-93985
date: 2026-08-19
---

**What is true** — `ThemeCssBuilder.ApplyFonts` validates every non-default family in its own loop
*before* the `NeedsImport` decision, and `FontFamilyName.IsValid` compares the raw value against its
own trimmed form in addition to matching the grammar. Both look redundant and both are load-bearing.

**Why it is this way** — a family that `NeedsImport` suppresses (today: one the Google Fonts probe
reported as not published) never reaches `FontImportBuilder`, but `ReplaceFontFamily` still writes it
verbatim into the `--crt-font-family-*` token. `FontFamilyName`'s regex is the only font-family
validation in the codebase, so on the earlier design — where validation happened as a side effect of
building the import URL — a suppressed family was written unvalidated. As for the padding check: the
grammar is `^[A-Za-z0-9][A-Za-z0-9 -]*\z` and a space is inside the allowed character class, so a
**trailing** space matches the pattern happily; only leading whitespace and newlines fail it.

**What breaks if you ignore it** — moving validation back behind the import decision makes
`--heading-font "Evil'; } body { background-image: url(...) } .z { a: b"` exit 0 and emit a token that
closes the theme rule, injecting unscoped global CSS into a stylesheet served to every user of the
environment; the same input on the import path exits 1 with `INVALID_FONT_FAMILY`. Dropping the
`family == family.Trim()` comparison on the assumption that the regex covers padding lets
`"Verdana "` through, where the quoted family is matched literally by the browser and silently falls
back to a generic face - and a trailing newline produces a bad string token that drops that
declaration and the one after it.
