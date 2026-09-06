---
description: BfEmailTemplateExtendedCopyService and BFTemplateTransformerService are not public REST endpoints for email-template copy
applies-to:
  - clio/Command/McpServer/Tools/EmailTemplateTool.cs
date: 2026-08-29
---

**What is true** — Creatio 10.1 source inspection found `BfEmailTemplateExtendedCopyService` as an
internal `IExtendedEmailCopyService` implementation and `BFTemplateTransformerService` as a static
transformation helper. Neither exposes a supported public REST route that Clio can invoke.

**Why it is this way** — the platform's own copy workflow can coordinate Beefree rows and related
display-condition metadata inside the application, but those implementation types are not an
external integration contract. Clio therefore copies exposed content by guarded read/update and
does not invent a `call-service` URL.

**What breaks if you ignore it** — a guessed endpoint fails at runtime, while copying only
`BfEmailTemplate` content can omit platform-internal conditional-display relationships. Report that
limitation and require designer verification when conditional content is involved.
