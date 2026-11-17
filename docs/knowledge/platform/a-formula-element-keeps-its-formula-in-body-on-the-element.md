---
description: a Formula element stores its formula in `body` on the flow element itself (and its target in `resultParameterMetaPath`); its `parameters` array is empty, so a scan of parameter values misses what a formula reads
applies-to:
  - clio/Command/ProcessModel/Schema.cs
  - clio/Command/ProcessModel/ProcessCredentialSources.cs
ticket: ENG-105475
date: 2026-11-16
---

**What is true** — in a process schema read with `ProcessSchemaRequest`, a Formula element
(`ProcessSchemaFormulaTask`, manager `D334D28F-...`) carries `"body": "[#SysSettings.UsrRpmApiKey#]"` (or any other
formula) and `"resultParameterMetaPath": "<parameter UId>"` as members of the flow element, with an empty `parameters`
array (measured 2026-11-16 on env `Creatio`, CrtProcessBuilder 1.6.6.172 and .173, on Formula elements built by
create-business-process).

**Why it is this way** — the platform serializes the element's own properties next to its identity; only elements
with a parameter list (user tasks, web service calls) keep their values in `parameters`.

**What breaks if you ignore it** — a schema scan that reads only parameter values misses a formula: run-process's
credential check would not see a SecureText setting a Formula element copies into a result, and the decrypted value
would be returned in clear.
