---
description: ExceptionReadableMessageExtension drops a wrapper's message when that message contains the text below it, so a wrapper that INTERPOLATES the failure it wraps silently loses everything it added; chaining keeps every link, but only since issue #1376 - before that only the outermost wrapper survived
applies-to:
  - clio/ExceptionReadableMessageExtension.cs
  - clio/Common/CompilationHistoryPoller.cs
  - clio/Package/PackageBuilder.cs
  - clio/Command/CompileConfigurationCommand.cs
ticket: "#1376"
date: 2026-09-11
---

**What is true** — `ExceptionReadableMessageExtension.DescribeOuterContext` returns `null` for a link
whose `Message` contains the carrier's (`outer.Message.Contains(carrierMessage)`), and that link is then
not printed. So a wrapper built as

```csharp
throw new InvalidOperationException($"… gave up after {elapsed} s …. Last failure: {Describe(inner)}", inner);
```

prints only `Describe(inner)` — the elapsed time, the window and the round count it was written to carry
never reach the operator. **Chain the inner exception and keep it out of the message text**; then
`DescribeChainAboveCarrier` walks every link from the outermost down to the carrier and joins the
non-duplicate messages with `": "`, so all three of `build-package`'s links print:

```
Package compilation could not be monitored: Compilation polling gave up after 93 s … (give-up window
90 s, 21 failed rounds): Failed reading records from entity schema 'CompilationHistory': …
```

That walk is what issue #1376 added. **Before it, only the OUTERMOST wrapper survived as a prefix and
every intermediate one was dropped even when correctly chained** — a two-link chain rendered fully and a
three-link chain silently lost its middle, which is precisely where `PackageBuilder` puts the poller's
diagnosis.

A wrapper whose inner carries no server detail at all takes a different arm (`ComposeWithInnerDetail`):
the wrapper's own message leads and the inner follows, scrubbed. That arm used to return the inner
message alone — raw — and `ClassifyingDataProvider.Guard` rethrows transport faults UNCHANGED, so an
`HttpRequestException` under a wrapper took exactly that path.

**Why it is this way** — the duplicate rule exists so a wrapper that merely repeats the sentence below
it is not printed twice, which is the common case across the ~20 commands this renderer serves. It
cannot tell a repetition apart from a message that quotes the inner as one clause among several.

**What breaks if you ignore it** — observed live on `ts1-core-dev04` while verifying issue #1376: the
poll's give-up exception was composed with the last failure interpolated, and `clio cc` printed
`Compilation progress could not be monitored: Failed reading records from entity schema
'CompilationHistory': …` with no mention of the 90-second window or the 21 failed rounds. Nothing fails,
nothing logs, and the diagnostic simply is not there.
