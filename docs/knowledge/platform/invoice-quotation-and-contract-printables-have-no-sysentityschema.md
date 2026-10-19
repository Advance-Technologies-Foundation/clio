---
description: the shipped MS Word printables Invoice, Quotation and Contract carry no SysEntitySchema in SysModuleReport - their object resolves through SysModule.SysModuleEntity (the run time's GetReportEntitySchemaName fallback); ConvertInPDF is false on the shipped Word printables
applies-to:
  - clio/Command/ListPrintablesCommand.cs
  - clio.mcp.e2e/Support/Results/ListPrintablesEnvelope.cs
  - clio.mcp.e2e/ProcessFileReportAndParameterToolE2ETests.cs
ticket: ENG-96506
date: 2026-10-19
---

**What is true** — read on a plain product install on 2026-10-01 and again on 2026-10-19: the `SysModuleReport` rows
of the shipped Invoice, Quotation and Contract MS Word printables have an empty `SysEntitySchema`; their object is
the section's, reached through `SysModule.SysModuleEntity` - the fallback the run time's
`GetReportEntitySchemaName` takes. A printable added for an object of its own (for example the DNS ones, on
`DNSGuideReport`) carries `SysEntitySchema` directly. `ConvertInPDF` is false on every shipped Word printable read.

**Why it is this way** — the shipped printables are registered against their section, not against an object.

**What breaks if you ignore it** — a lookup of a printable by `entitySchemaName` alone finds none for Invoice,
Quotation or Contract, so a test or probe skips or fails for a printable that exists; read the section's object
too (`moduleEntitySchemaName` in `list-printables`). And the "needs a PDF converter" notice cannot be triggered by a
shipped printable: it needs one created with PDF conversion on.
