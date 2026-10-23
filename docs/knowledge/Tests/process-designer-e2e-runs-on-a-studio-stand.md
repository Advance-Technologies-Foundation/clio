---
description: both MCP e2e TeamCity configurations deploy Studio, so a McpE2E.ProcessDesigner test that reads Sales or Service content (Invoice, Case, InvoiceVisaProcess, a stock email template) fails in setup there; such a test builds what it reads
applies-to:
  - clio.mcp.e2e/Support/Mcp/EmailTemplateE2EArrange.cs
  - clio.mcp.e2e/CreateBusinessProcessToolE2ETests.cs
  - clio.mcp.e2e/DescribeProcessToolE2ETests.cs
  - clio.mcp.e2e/ModifyBusinessProcessToolE2ETests.cs
  - clio.mcp.e2e/ModifyProcessAsNewVersionToolE2ETests.cs
  - clio.mcp.e2e/ProcessFileReportAndParameterToolE2ETests.cs
  - clio.mcp.e2e/ProcessToolArgumentAliasesE2ETests.cs
  - .github/workflows/teamcity-mcp-e2e.yml
ticket: ENG-103923
date: 2026-10-21
---

**What is true** - both MCP e2e TeamCity configurations deploy the Studio product on PostgreSQL:
`Team_Atf_ClioMcpE2eTests`, whose pull-request runs `.github/workflows/teamcity-mcp-e2e.yml` queues with
`ProductName=Studio`, and `TestLab_LoadTesting_ClioMcpTest_ClioMcpE2eProcessDesigner`, copied from it to run the
`McpE2E.ProcessDesigner` category. Studio has no Invoice or Case object, no `InvoiceVisaProcess`, no MS Word
printable and none of the Service email templates. A test therefore builds what it reads (`EmailTemplateE2EArrange`
writes an email template, Contact or Account stands in for Invoice) or skips with an `Assert.Ignore` that names the
missing content.

**Why it is this way** - the ProcessDesigner configuration was copied from `Team_Atf_ClioMcpE2eTests`, and the
product is set in two places: each configuration's `ProductName` default and the `ProductName` the workflow sends.

**What breaks if you ignore it** - the test fails in setup on the lane (`Item with name "Invoice" not found`,
`Process 'InvoiceVisaProcess' was not found`, `no email template is named ...`) and says nothing about the tool it
was written for. On a developer stand with a full product the same test passes, so its author does not see it.
