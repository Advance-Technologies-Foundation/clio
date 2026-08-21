---
description: crt.HandleViewModelAttributeChangeRequest fires on EVERY attribute change and requestArgumentPropertyName does not scope it - the only scoping is an in-body request.attributeName guard
applies-to:
  - clio/Command/McpServer/Tools/PageBodyAstLinter.cs
  - clio.tests/Command/McpServer/PageBodyAstLinterTests.cs
ticket: ENG-95557
date: 2026-08-19
---

**What is true** — on a Freedom UI page, a `crt.HandleViewModelAttributeChangeRequest` handler is
invoked for **every** view-model attribute change, not only for the attribute the author had in
mind. The one thing that scopes it is an in-body guard on the request itself:
`if (request.attributeName !== "<Attr>") return next?.handle(request);`. The
`requestArgumentPropertyName` key, which looks like the scoping mechanism and is used to scope other
request types, is **silently ignored** for this request.

**Why it is this way** — this is Creatio platform behaviour, not clio behaviour. The handler
registration carries no attribute filter, so the platform dispatches the request to every registered
handler and leaves the filtering to the handler body.

**What breaks if you ignore it** — a handler that writes an attribute through `$context.set(...)`
re-fires on its own write, so an `else` branch clears the field it just set (ENG-95557: a
phone-number page silently wiped `UsrCountryCode` at runtime). The page compiles, saves and renders;
only the runtime value is wrong, which is why the defect was root-caused on a live stand rather than
from the symptom. `PageBodyAstLinter`'s `RuleHandlerAttributeChangeUnscopedWrite` warning is the
guard that catches this at `update-page` / `validate-page` / `sync-pages` time — it exists because
neither the page schema nor any platform error reports the problem, so do not remove it as
redundant.
