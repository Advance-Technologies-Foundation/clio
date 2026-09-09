---
description: crt.OpenPageRequest params.schemaName on a WEB page always names a web page, which is why the converter settles that target offline and removes the binding with no environment read - the two-tier SysSchema/GetParentSchemas classifier built to verify it was deleted because the answer is fixed
applies-to:
  - clio/Command/McpServer/Tools/MobilePageConverter/MobileActionTargetProbe.cs
  - clio/Command/McpServer/Data/WebToMobilePageConversionRules.json
ticket: ENG-94839
date: 2026-09-09
---

**What is true** — the invariant `MobileActionTargetProbe.KindWebPage` rests on holds only because of two
things outside that file: the converter accepts a **web** source page only, and `crt.OpenPageRequest`'s
rule in `WebToMobilePageConversionRules.json` declares no `paramMap`, so the value is carried verbatim.
Given both, plus client-unit schema names being unique per manager, a web page's `schemaName` names a WEB
page and nothing else.

**Why it is this way** — ENG-94839 first built a two-tier `SysSchema` + `GetParentSchemas` classifier to
*verify* whether the named schema was a mobile page, then deleted it: the question had a fixed answer, and
the round trips bought only a way to be wrong. (The platform reason a bulk web-vs-mobile classification is
hard at all is recorded in `client-unit-schema-type-is-not-a-sysschema-column.md`.) That deletion is what
makes this the one verdict strong enough to remove an action — it needs no environment call, so it cannot
be wrong for a reason outside the process, and it survives an unreachable environment.

**What breaks if you ignore it** — widen the converter to accept a MOBILE source page, or give
`crt.OpenPageRequest` a `paramMap` that rewrites `schemaName`, and the assumption stops holding while the
code keeps removing bindings with no environment evidence and no way to notice. Either change must
re-derive the verdict from the environment or drop `web-page` out of
`MobileActionTargetProbe.StripsBindingOnMissing`.
