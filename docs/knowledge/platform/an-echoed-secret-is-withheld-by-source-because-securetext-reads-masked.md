---
description: run-process cannot mask a key a web service echoed back by its value - a SecureText system setting reads as *** through clio - so a result fed by a Call web service element is withheld by its source instead
applies-to:
  - clio/Command/ProcessModel/ProcessResultSources.cs
  - clio/Command/RunProcessCommand.cs
ticket: ENG-105475
date: 2026-11-16
---

**What is true** — a Call web service element's `Response` output is the whole response body, and an API that
echoes its request (httpbin `/anything`, many debug and error endpoints) puts the `Authorization` header and every
header the platform filled in from a system setting into it. Mapped into a process parameter, it came back through
`run-process` in clear (measured 2026-11-16 on env `Creatio`, .NET Framework: an `X-Api-Key` from a SecureText
setting and `Authorization: Basic ...` from the service's Basic auth, directly, through a sub-process and through a
Formula element). clio cannot recognise such a key by value: `get-sys-setting` returns `***` for a SecureText
setting (measured the same day; a Text setting comes back readable). `test-web-service-method` masks by value only
because the platform reports the request it sent there; a process run reports nothing of the kind.

**Why it is this way** — the decrypted value is not handed out, and clio must not hold it; a key echoed in another
form (a Guid, a number, a fragment) would still pass a value match.

**What breaks if you ignore it** — masking by value leaves every SecureText key in the answer, which is the case the
web service guidance recommends for keys.
