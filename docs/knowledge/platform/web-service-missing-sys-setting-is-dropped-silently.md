---
description: A web service parameter bound to a missing or empty system setting is sent without that header or parameter, with no error
applies-to:
  - clio/Command/WebServiceDesigner/WebServiceDesigner.cs
ticket: ENG-105475
date: 2026-11-11
---

**What is true** — at run time the platform resolves a parameter default that references a system setting; when
the setting does not exist, or exists with an empty value, the header, query value or cookie is simply not sent.
Nothing is logged and the call proceeds.

**Why it is this way** — the platform treats an unresolved default like an unset optional parameter.

**What breaks if you ignore it** — a service registered with a typo in a setting code, or before the user filled the
setting, calls the API without its key; the API answers 401/403 and the process author looks for the fault in the
API. clio refuses a reference to a setting that does not exist (`RequireSysSettings`), and the guide tells the agent
that a 401 on a fresh service usually means the setting value is still empty.
