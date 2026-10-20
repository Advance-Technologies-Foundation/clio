---
description: the clio environment Creatio (the .NET Framework sandbox used for process-builder work) carries exactly five MS Word printables - Contract, DNS Specifications, DNS Requirements, Invoice, Quotation - and no FastReport or DevExpress printable or package
applies-to:
  - clio.mcp.e2e/ProcessFileReportAndParameterToolE2ETests.cs
  - clio/Command/ListPrintablesCommand.cs
ticket: ENG-96506
date: 2026-10-19
---

**What is true** — read on 2026-10-01 and re-read on 2026-10-19 (CrtProcessBuilder 1.6.6.104) on the clio
environment `Creatio`, the .NET Framework sandbox used for process-builder work: its `SysModuleReport` holds five MS
Word printables, Contract, DNS Specifications, DNS Requirements, Invoice and Quotation, all with `ConvertInPDF`
false. It has no FastReport or DevExpress printable and no FastReport package. Why three of them have no object of
their own is a product fact, recorded in
`../platform/invoice-quotation-and-contract-printables-have-no-sysentityschema.md`.

**Why it is this way** — it is a plain product install; nobody has added printables to it.

**What breaks if you ignore it** — the PDF-converter notice and the FastReport and DevExpress refusals cannot be
proven on this sandbox; they rest on unit tests. The e2e cases that need a printable Ignore themselves when
`list-printables` returns no row, so a run here proves the MS Word path only.
