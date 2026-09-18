---
description: a static readonly Regex in WebToMobileAnalysisService declared ABOVE the RegexTimeout field reads TimeSpan.Zero, which Regex rejects - the throw happens in the type initializer, so every test that touches the converter fails and none of them names the regex
applies-to:
  - clio/Command/McpServer/Tools/MobilePageConverter/WebToMobileAnalysisService.cs
ticket: ENG-95827
date: 2026-09-15
---

**What is true** — `WebToMobileAnalysisService` has several `static readonly Regex` fields that all
pass one `static readonly TimeSpan RegexTimeout` as the match-timeout argument. C# initializes
static fields in **declaration order**, so a regex field declared above `RegexTimeout` reads it as
`default(TimeSpan)` — `TimeSpan.Zero` — and `new Regex(pattern, options, TimeSpan.Zero)` throws
`ArgumentOutOfRangeException`.

Keep every regex field below `RegexTimeout`, next to the ones already there.

**Why it is this way** — the timeout is a field rather than a `const` because `TimeSpan` cannot be
one, so it is subject to initialization order in a way a `const int` budget beside it is not.

**What breaks if you ignore it** — the exception is thrown from the **type initializer**, so it does
not surface at the regex. Every call into the class fails with
`TypeInitializationException`, and the first run after adding the field shows ~345 unit tests
failing across passes that have nothing to do with the new pattern — spacing normalization, request
conversion, adaptive layout. Nothing in that output names the field, and the natural reading is that
the change broke the converter rather than that a field moved too high in the file.
