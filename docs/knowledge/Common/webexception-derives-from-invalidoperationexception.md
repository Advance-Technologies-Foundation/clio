---
description: System.Net.WebException derives from InvalidOperationException, so `is InvalidOperationException` as a "resource not found" discriminator also swallows DNS, TLS and connection-reset faults - use exact-type equality, and unwrap AggregateException because Creatio's client runs via Task.Result
applies-to:
  - clio/Command/EntitySchemaDesigner/RemoteEntitySchemaColumnManager.cs
  - clio/Command/EntitySchemaDesigner/EntitySchemaPublisher.cs
  - clio/Common/ObjectRights/ObjectRightsSupport.cs
date: 2026-09-03
---

**What is true** — `System.Net.WebException` inherits from `InvalidOperationException`. Any catch
filter or predicate that uses `exception is InvalidOperationException` to mean "the reader reported
no such resource" therefore also matches every transport fault raised as a `WebException`: DNS
failure, TLS failure, connection reset. Where the distinction matters the test must be exact-type
equality — `exception.GetType() == typeof(InvalidOperationException)` — which is what
`RemoteEntitySchemaColumnManager` does for its "no runtime schema came back" branch. An allow-list
that genuinely wants transport faults included (`EntitySchemaPublisher.IsExpectedODataBuildFault`)
enumerates `WebException` explicitly instead of relying on the inheritance.
Merged schema and merged column discovery intentionally share `ReadMergedRuntimeSchema`, whose purpose is
to normalize all supported reader and transport failures; it does not treat `InvalidOperationException`
as a not-found discriminator.

**Why it is this way** — the .NET type hierarchy, nothing clio chose. Compounding it, Creatio's
client is driven through `Task.Result`, so faults arrive wrapped in `AggregateException`; both
predicates above unwrap it recursively before classifying, and so do the object-rights
`ObjectRightsSupport.IsServiceFailure` / `IsTimeout` (a wrapper is a service failure only when every
fault in it is one, and a timeout when any is).

**What breaks if you ignore it** — an unreachable or misconfigured environment is classified as
"this schema is simply not compiled yet", the guard silently takes its tolerant branch, and clio
proceeds to write against a state it never actually read. Conversely, narrowing the catch without the
`AggregateException` unwrap turns a skippable post-publish check into an aborted command — for
`set-object-rights` it skipped the read-back of a save that timed out but may have committed, and for
`get-object-rights --include-connected` the first hanging lookup ended the whole listing.
